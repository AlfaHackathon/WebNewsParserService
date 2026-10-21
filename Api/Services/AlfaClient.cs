using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models.Alfa;
using Api.Services.Interfaces;
using Api.Settings;
using Microsoft.Playwright;

namespace Api.Services;

/// <summary>
/// Клиент «Альфа-Инвестора» (https://alfabank.ru/alfa-investor/).
///
/// У сайта нет публичного API, а новости обычным HTTP-клиентом не отдаются:
/// 1) страница спрятана за антибот-челленджем ServicePipe (аналог Cloudflare);
/// 2) TLS-сертификат выпущен НУЦ Минцифры (Russian Trusted Sub CA).
///
/// Поэтому весь ход через Playwright (headless Chromium): браузер сам проходит
/// челлендж, --ignore-certificate-errors закрывает вопрос с российским УЦ,
/// а дальше забираем данные, как делает их собственный фронтенд:
/// - лента: их внутренний GraphQL POST /api/v1/eye-of-investor/graphql
///   (запрос GetFeedPublications, анонимный доступ, курсор-пагинация);
/// - полный текст статьи: в HTML статьи вшито SSR-состояние (script#app_state),
///   внутри pageContent.fetchResults[].response.value.article — title,
///   body (markdown), publishAt, author, assets.
/// </summary>
public class AlfaClient(AlfaClientSettings settings, ILogger<AlfaClient> logger) : IAlfaClient, IAsyncDisposable
{
    /// <summary>Путь до ленты новостей «Альфа-Инвестора».</summary>
    private const string FeedPagePath = "/alfa-investor/category/t/news/all-posts/";

    /// <summary>Их внутренний GraphQL-эндпоинт (ходим с той же origin, что и сайт).</summary>
    private const string GraphqlPath = "/api/v1/eye-of-investor/graphql";

    /// <summary>Как у TASS: настоящий браузерный User-Agent.</summary>
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    /// <summary>
    /// Запрос GetFeedPublications — в точности как его делает страница
    /// (снято с их фронта, работает в анонимном режиме и с курсором).
    /// </summary>
    private const string FeedQuery = """
        query GetFeedPublications($page: CursorPageFilter, $authorUuid: UuidEq, $isAuthorized: Boolean = false, $filterType: StringFilterEq, $rubricCodes: StringFilterIn) {
          getFeedPublications(
            page: $page
            filter: {authorUuid: $authorUuid, filterType: $filterType, rubricCodes: $rubricCodes}
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

    /// <summary>JS: POST на их GraphQL прямо из контекста страницы (с её куками — антибот не отличает).</summary>
    private const string FetchGraphqlJs = """
async payload => {
    try {
        const res = await fetch('/api/v1/eye-of-investor/graphql', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: payload
        });
        return { status: res.status, body: await res.text() };
    } catch (e) {
        return { status: 0, body: String(e) };
    }
}
""";

    /// <summary>JS: вытащить объект статьи из вшитого SSR-состояния страницы.</summary>
    private const string ExtractArticleJs = """
() => {
    const el = document.querySelector('#app_state');
    if (!el) return null;
    const st = JSON.parse(el.textContent);
    const results = (st.pageContent && st.pageContent.fetchResults) || [];
    for (const result of results) {
        const article = result && result.response && result.response.value && result.response.value.article;
        if (article) return article;
    }
    return null;
}
""";

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    /// <summary>Все операции подряд по одной странице — гонки не нужны, но подстрахуемся.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    public async Task<AlfaFeed?> GetFeedAsync(string? cursor, CancellationToken cancellationToken = default)
    {
        // shape переменных — 1:1 как шлёт их фронтенд (см. пагинацию "Показать ещё")
        var payload = JsonSerializer.Serialize(new GraphqlRequest(
            FeedQuery,
            new FeedVariables(
                new PageFilter(cursor, settings.Limit),
                false,
                new RubricCodesFilter([settings.Rubric]),
                new FilterTypeFilter("rubrics"))), JsonWeb);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var page = await OpenPageAsync(FeedPagePath, cancellationToken);
            var http = await page.EvaluateAsync<GraphqlHttpResult?>(FetchGraphqlJs, payload);
            if (http is null || http.Status != 200 || string.IsNullOrEmpty(http.Body))
            {
                logger.LogWarning("GraphQL альфа-лента: статус {Status}, тело пустое", http?.Status ?? 0);
                return null;
            }

            var response = JsonSerializer.Deserialize<AlfaFeedResponse>(http.Body, JsonWeb);
            return response?.Data?.Feed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось забрать ленту альфа-новостей");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlfaArticle?> GetArticleAsync(string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var page = await OpenPageAsync($"/alfa-investor/t/{path.Trim('/')}/", cancellationToken);
            var articleJson = await page.EvaluateAsync<JsonElement?>(ExtractArticleJs);
            if (articleJson is null || articleJson.Value.ValueKind == JsonValueKind.Null)
            {
                logger.LogWarning("В SSR-состоянии статьи альфа нет объекта article (/{Path})", path);
                return null;
            }

            return JsonSerializer.Deserialize<AlfaArticle>(articleJson.Value, JsonWeb);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось открыть страницу альфа-статьи (/{Path})", path);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_page is not null)
        {
            try { await _page.CloseAsync().ConfigureAwait(false); } catch { /* уже закрыта */ }
        }

        if (_browser is not null)
        {
            try { await _browser.CloseAsync().ConfigureAwait(false); } catch { /* уже закрыт */ }
        }

        if (_playwright is not null)
        {
            try { await _playwright.DisposeAsync().ConfigureAwait(false); } catch { /* уже почищено */ }
        }

        _gate.Dispose();
    }

    /// <summary>
    /// Гарантирует браузер+страницу и что она открыта по нужному пути:
    /// ленивая инициализация + переход с прохождением антибот-челленджа.
    /// </summary>
    private async Task<IPage> OpenPageAsync(string path, CancellationToken cancellationToken)
    {
        if (_page is null)
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,

                // --no-sandbox: в docker запускаемся не под root;
                // --ignore-certificate-errors: сертификат НУЦ Минцифры не в доверенных;
                // остальное — чисто от ботов.
                Args = ["--no-sandbox", "--disable-dev-shm-usage", "--ignore-certificate-errors", "--disable-blink-features=AutomationControlled"]
            });
            _page = await _browser.NewPageAsync(new BrowserNewContextOptions
            {
                UserAgent = UserAgent,
                Locale = "ru-RU",
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
            });

            logger.LogInformation("Playwright Chromium запущен для alfabank.ru");
        }

        var url = settings.BaseAddress.TrimEnd('/') + path;

        // до 3 попыток: челлендж после прохождения сам перезагружает страницу,
        // «navigation interrupted» при этом — норма, ретраим
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit, Timeout = 60_000 });
            }
            catch (Exception exception) when (attempt < 3)
            {
                logger.LogWarning(exception, "Навигация на {Url} оборвалась челленджем, попытка {Attempt}", url, attempt);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            // до N секунд ждём, когда на странице появится вшитое SSR-состояние:
            // пока висит челлендж — его нет, как только пропустили — сразу появляется
            try
            {
                await _page.WaitForSelectorAsync("script#app_state",
                    new PageWaitForSelectorOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = settings.ChallengeWaitTimeoutSeconds * 1000
                    });
                return _page;
            }
            catch (TimeoutException) when (attempt < 3)
            {
                logger.LogWarning("Челлендж альфы не пропущен за {Seconds} с, попытка {Attempt}",
                    settings.ChallengeWaitTimeoutSeconds, attempt);
            }
        }

        throw new TimeoutException(
            $"Антибот-челлендж alfabank.ru не пропущен за {settings.ChallengeWaitTimeoutSeconds} с (3 попытки)");
    }

    // --- сериализация запроса/ответа GraphQL ---

    private sealed record GraphqlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] FeedVariables Variables);

    private sealed record FeedVariables(
        [property: JsonPropertyName("page")] PageFilter Page,
        [property: JsonPropertyName("isAuthorized")] bool IsAuthorized,
        [property: JsonPropertyName("rubricCodes")] RubricCodesFilter RubricCodes,
        [property: JsonPropertyName("filterType")] FilterTypeFilter FilterType);

    private sealed record PageFilter(
        [property: JsonPropertyName("cursor")] string? Cursor,
        [property: JsonPropertyName("limit")] int Limit);

    private sealed record RubricCodesFilter(
        [property: JsonPropertyName("in")] string[] In);

    private sealed record FilterTypeFilter(
        [property: JsonPropertyName("eq")] string Eq);

    private sealed record GraphqlHttpResult(int Status, string? Body);
}
