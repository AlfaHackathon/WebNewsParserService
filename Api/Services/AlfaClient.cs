using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Api.Models.Alfa;
using Api.Services.Interfaces;
using Api.Settings;
using Microsoft.Playwright;

namespace Api.Services;

/// <summary>
/// Клиент «Альфа-Инвестора» (https://alfabank.ru/alfa-investor/), гибридная схема.
///
/// Что за проблема: у сайта нет публичного API, а на прямой HTTP-запрос он отвечает
/// заглушкой антибот-челленджа ServicePipe (аналог Cloudflare): контент выдаётся
/// только «настоящему» браузеру после выполнения JavaScript. Плюс TLS-сертификат
/// выпущен НУЦ Минцифры и по умолчанию не в доверенных.
///
/// Как решаем (гибрид B):
/// 1. Основной транспорт — обычный HttpClient. Антибот-челлендж пропускается
///    по кукам антибот-сессии (spid/spsn и иже), она живёт ограниченное время.
///    Куки достаются вручную из браузера разово (Alfa:ManualCookies) и дальше
///    <see cref="CookieContainer"/> сам их поддерживает из ответов сайта.
///    Сертификат Минцифры — отключение проверки в HttpClientHandler (в Docker
///    ради хакатонского прототипа проще программного обхода, чем возни с хранилищем).
/// 2. Страховка: если транспорт получил заглушку (куки протухли), раз в минуту
///    максимум поднимается headless Chromium (Playwright), проходит челлендж и
///    свежие куки подмешиваются в <see cref="CookieContainer"/> — дальше HttpClient
///    снова работает без браузера.
///
/// Данные, как их делает собственный фронтенд альфы:
/// - лента: их внутренний GraphQL POST /api/v1/eye-of-investor/graphql
///   (запрос GetFeedPublications, анонимный доступ, курсор-пагинация);
/// - полный текст статьи: в HTML статьи вшито SSR-состояние (script#app_state),
///   внутри pageContent.fetchResults[].response.value.article — там title,
///   body (markdown), publishAt, author, assets.
/// </summary>
public class AlfaClient : IAlfaClient, IAsyncDisposable
{
    /// <summary>Путь до ленты новостей «Альфа-Инвестора».</summary>
    private const string FeedPagePath = "/alfa-investor/category/t/news/all-posts/";

    /// <summary>Их внутренний GraphQL-эндпоинт (ходим с той же origin, что и сайт).</summary>
    private const string GraphqlPath = "/api/v1/eye-of-investor/graphql";

    /// <summary>Заголовки как у их же фронтенда (снято из запросов сайта) — часть пропуска WAF.</summary>
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36";

    private const string SecChUa = "\"Google Chrome\";v=\"155\", \"Chromium\";v=\"155\", \"Not(A:Brand\";v=\"24\"";

    /// <summary>Маркер заглушки антибот-челленджа в теле ответа (обрывок их обёртки-спиннера).</summary>
    private const string ChallengeMarker = "js-challenge-loader";

    /// <summary>
    /// Запрос GetFeedPublications — в точности как его делает страница
    /// (снято с их фронта, работает в анонимном режиме и с курсором).
    /// </summary>
    private const string FeedQuery = """
        query GetFeedPublications($page: CursorPageFilter, $filterType: StringFilterEq, $rubricCodes: StringFilterIn) {
          getFeedPublications(
            page: $page
            filter: {filterType: $filterType, rubricCodes: $rubricCodes}
          ) {
            pagination {
              nextCursor
              hasNextPage
            }
            publications {
              publicationType: __typename
              uuid
              publishAt
              author {
                uuid
                username
              }
              assets(filter: {isActive: {eq: true}}) {
                id
                name
                ISIN
                ticker
                assetType {
                  name
                  code
                }
              }
              rubric {
                name
                code
              }
              ... on Article {
                title
                description
                path
              }
              ... on Post {
                body
                title
              }
            }
          }
        }
        """;

    /// <summary>Не чаще раза в это время дёргаем браузер ради свежих кук.</summary>
    private static readonly TimeSpan CookieRefreshPause = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly AlfaClientSettings _settings;
    private readonly ILogger<AlfaClient> _logger;

    /// <summary>Все операции пока последовательно (один воркер) — но гонки шлем под замок.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    // --- HTTP: основной транспорт ---
    private readonly CookieContainer _cookies = new();
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _http;
    private bool _manualCookiesApplied;

    // --- Playwright: только для обновления протухших кук ---
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private DateTime? _lastCookieRefreshUtc;

    public AlfaClient(AlfaClientSettings settings, ILogger<AlfaClient> logger)
    {
        _settings = settings;
        _logger = logger;

        _handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,

            // сертификат НУЦ Минцифры по умолчанию не в доверенных (и системное
            // хранилище от имени контейнера менять лишнее) — отключаем проверку
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(_handler, disposeHandler: true)
        {
            BaseAddress = new Uri(settings.BaseAddress),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", SecChUa);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");

        // без этого набора WAF режет по заголовкам — экспериментально:
        // [куки без заголовков] -> 403, [куки + браузерные заголовки] -> 200
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", settings.BaseAddress.TrimEnd('/'));
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", settings.BaseAddress.TrimEnd('/') + FeedPagePath);

        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<AlfaFeed?> GetFeedAsync(string? cursor, CancellationToken cancellationToken = default)
    {
        // shape переменных — 1:1 как шлёт их фронтенд (см. пагинацию «Показать ещё»)
        var request = JsonSerializer.Serialize(new GraphqlRequest(
            FeedQuery,
            new FeedVariables(
                new PageFilter(cursor, _settings.Limit),
                new RubricCodesFilter([_settings.Rubric]),
                new FilterTypeFilter("rubrics"))), JsonWeb);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var body = await FetchAsync(
                () => PostGraphqlAsync(request, cancellationToken), "лента", cancellationToken);
            if (body is null)
            {
                return null;
            }

            var response = JsonSerializer.Deserialize<AlfaFeedResponse>(body, JsonWeb);
            return response?.Data?.Feed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlfaArticle?> GetArticleAsync(string path, CancellationToken cancellationToken = default)
    {
        var articleUrl = $"/alfa-investor/t/{path.Trim('/')}/";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var html = await FetchAsync(
                () => GetStringAsync(articleUrl, cancellationToken), $"статья {path}", cancellationToken);
            if (html is null)
            {
                return null;
            }

            var articleJson = ExtractArticleJson(html);
            if (articleJson is null || articleJson.Value.ValueKind == JsonValueKind.Null)
            {
                _logger.LogWarning("В SSR-состоянии альфа-статьи нет объекта article ({Url})", articleUrl);
                return null;
            }

            return JsonSerializer.Deserialize<AlfaArticle>(articleJson.Value, JsonWeb);
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- транспорт с одной автоматической переустановкой кук при встрече челленджа ---

    private async Task<string?> FetchAsync(Func<Task<string?>> request, string what, CancellationToken cancellationToken)
    {
        var body = await EnsureCookiesAndRunAsync(request, what, cancellationToken);
        if (body is not null)
        {
            return body;
        }

        // куки протухли: свежая порция через браузер (rate-limit внутри) и вторая попытка
        await RefreshCookiesViaBrowserAsync(cancellationToken);
        return await EnsureCookiesAndRunAsync(request, what, cancellationToken);
    }

    private async Task<string?> EnsureCookiesAndRunAsync(Func<Task<string?>> request, string what, CancellationToken cancellationToken)
    {
        ApplyManualCookiesOnce();

        var body = await request();
        if (body is null)
        {
            return null;
        }

        if (body.Contains(ChallengeMarker, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Альфа {What}: ответ — антибот-челлендж (куки протухли?)", what);
            return null;
        }

        return body;
    }

    private void ApplyManualCookiesOnce()
    {
        if (_manualCookiesApplied || string.IsNullOrWhiteSpace(_settings.ManualCookies))
        {
            _manualCookiesApplied = true;
            return;
        }

        // формат — как Cookie-заголовок из DevTools браузера: "spid=...; spsn=...; ..."
        try
        {
            _cookies.SetCookies(new Uri(_settings.BaseAddress), _settings.ManualCookies);
            _logger.LogInformation("Альфа: применены куки из настроек ({Count} шт)",
                _settings.ManualCookies.Count(c => c == ';') + 1);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Альфа: не удалось применить ManualCookies — проверь формат");
        }
        finally
        {
            _manualCookiesApplied = true;
        }
    }

    private async Task<string?> PostGraphqlAsync(string requestJson, CancellationToken cancellationToken)
    {
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(GraphqlPath, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 403/401 — обычно тоже челлендж или отбнешь сессия: обрабатываем как заглушку
            _logger.LogWarning("GraphQL альфа-лента: статус {Status}", (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string?> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        // Accept как у браузера: страница — HTML (application/json даёт WAF 406)
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Accept.ParseAdd("application/xhtml+xml");
        request.Headers.Accept.ParseAdd("*/*;q=0.8");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GET альфа {Path}: статус {Status}", path, (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // --- вытащить состояние статьи из SSR-вёрстки ---

    private static JsonElement? ExtractArticleJson(string html)
    {
        var match = Regex.Match(
            html,
            "<script id=\"app_state\" type=\"application/json\">(.*?)</script>",
            RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        var state = JsonNode.Parse(match.Groups[1].Value);
        // fetchResults бывает в корне app_state и в pageContent (у разных страниц) — проверяем оба
        var fetchResults =
            state?["fetchResults"] as JsonArray
            ?? state?["pageContent"]?["fetchResults"] as JsonArray;
        if (fetchResults is null)
        {
            return null;
        }

        foreach (var fetchResult in fetchResults)
        {
            var article = fetchResult?["response"]?["value"]?["article"];
            if (article is not null)
            {
                return JsonSerializer.Deserialize<JsonElement>(article);
            }
        }

        return null;
    }

    // --- Playwright: разово пройти челлендж и забрать свежие куки ---

    private async Task RefreshCookiesViaBrowserAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (_lastCookieRefreshUtc is { } last && now - last < CookieRefreshPause)
        {
            _logger.LogInformation("Альфа: браузер для кук недавно запускался, пропускаю (rate-limit)");
            return;
        }

        _lastCookieRefreshUtc = now;

        _logger.LogInformation("Альфа: куки протухли, поднимаю Chromium для прохождения челленджа...");
        try
        {
            var page = await OpenChallengePageAsync(FeedPagePath, cancellationToken);
            var freshCookies = await page.Context.CookiesAsync();

            var applied = 0;
            foreach (var cookie in freshCookies)
            {
                var netCookie = new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain);
                if (cookie.Expires > 0)
                {
                    netCookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires).DateTime;
                }

                try
                {
                    _cookies.Add(netCookie);
                    applied++;
                }
                catch
                {
                    // кука с чужим доменом или протухшая — не критично
                }
            }

            _logger.LogInformation("Альфа: браузер отдал {Count} свежих кук, челлендж пройден", applied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Альфа: не удалось обновить куки через браузер. Если повторяется — обнови Alfa:ManualCookies вручную (DevTools → Network → Cookie)");
        }
    }

    /// <summary>Запустить Chromium, открыть страницу и дождаться прохождения антибот-челленджа.</summary>
    private async Task<IPage> OpenChallengePageAsync(string path, CancellationToken cancellationToken)
    {
        if (_playwright is null)
        {
            _playwright = await Playwright.CreateAsync();
        }

        if (_browser is null)
        {
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,

                // --no-sandbox: в docker запускаемся не под root;
                // --ignore-certificate-errors: сертификат НУЦ Минцифры не в доверенных;
                // остальное — чисто от ботов.
                Args = ["--no-sandbox", "--disable-dev-shm-usage", "--ignore-certificate-errors", "--disable-blink-features=AutomationControlled"]
            });

            _logger.LogInformation("Playwright Chromium запущен для alfabank.ru");
        }

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = UserAgent,
            Locale = "ru-RU",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        var page = await context.NewPageAsync();

        var url = _settings.BaseAddress.TrimEnd('/') + path;

        // до 3 попыток: челлендж после прохождения сам перезагружает страницу,
        // «navigation interrupted» при этом — норма, ретраим
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
            }
            catch (Exception exception) when (attempt < 3)
            {
                _logger.LogWarning(exception, "Навигация на {Url} оборвалась челленджем, попытка {Attempt}", url, attempt);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            // до N секунд ждём появления вшитого SSR-состояния:
            // пока висит челлендж — его нет, пропустили — сразу появляется
            try
            {
                await page.WaitForSelectorAsync("script#app_state",
                    new PageWaitForSelectorOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = _settings.ChallengeWaitTimeoutSeconds * 1000
                    });
                return page;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                _logger.LogWarning("Челлендж альфы не пропущен за {Seconds} с, попытка {Attempt}",
                    _settings.ChallengeWaitTimeoutSeconds, attempt);
            }
        }

        await context.DisposeAsync();
        throw new TimeoutException(
            $"Антибот-челлендж alfabank.ru не пропущен за {_settings.ChallengeWaitTimeoutSeconds} с (3 попытки)");
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            try { await _browser.DisposeAsync().ConfigureAwait(false); } catch { /* уже закрыт */ }
        }

        if (_playwright is not null)
        {
            try { _playwright.Dispose(); } catch { /* уже почищено */ }
        }

        _http.Dispose();
        _handler.Dispose();
        _gate.Dispose();
    }

    // --- сериализация запроса/ответа GraphQL ---

    private sealed record GraphqlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] FeedVariables Variables);

    private sealed record FeedVariables(
        [property: JsonPropertyName("page")] PageFilter Page,
        [property: JsonPropertyName("rubricCodes")] RubricCodesFilter RubricCodes,
        [property: JsonPropertyName("filterType")] FilterTypeFilter FilterType);

    private sealed record PageFilter(
        [property: JsonPropertyName("cursor")] string? Cursor,
        [property: JsonPropertyName("limit")] int Limit);

    private sealed record RubricCodesFilter(
        [property: JsonPropertyName("in")] string[] In);

    private sealed record FilterTypeFilter(
        [property: JsonPropertyName("eq")] string Eq);
}
