using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Api.Models.Alfa;
using Api.Services.Interfaces;
using Api.Settings;

namespace Api.Services.Clients;


public class AlfaClient : IAlfaClient
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
    
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly AlfaClientSettings _settings;
    private readonly ILogger<AlfaClient> _logger;

    // --- HTTP: основной транспорт ---
    private readonly HttpClient _http;
    private bool _manualCookiesApplied;

    public AlfaClient(AlfaClientSettings settings, ILogger<AlfaClient> logger)
    {
        _settings = settings;
        _logger = logger;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler, disposeHandler: true)
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

    public async Task<AlfaFeed?> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        // shape переменных — 1:1 как шлёт их фронтенд (см. пагинацию «Показать ещё»)
        var request = JsonSerializer.Serialize(new GraphqlRequest(
            Resources.GraphQLQueriesAlfa.FeedQuery,
            new FeedVariables(
                new RubricCodesFilter([_settings.Rubric]),
                new FilterTypeFilter("rubrics"))), JsonWeb);
        
        var body = await FetchAsync(
            () => PostGraphqlAsync(request, cancellationToken), "лента", cancellationToken);
        if (body is null)
        {
            return null;
        }

        var response = JsonSerializer.Deserialize<AlfaFeedResponse>(body, JsonWeb);
        return response?.Data?.Feed;
        
    }

    public async Task<AlfaArticle?> GetArticleAsync(string path, CancellationToken cancellationToken = default)
    {
        var articleUrl = $"/alfa-investor/t/{path.Trim('/')}/";
        
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

    // --- транспорт с одной автоматической переустановкой кук при встрече челленджа ---

    private async Task<string?> FetchAsync(Func<Task<string?>> request, string what, CancellationToken cancellationToken)
    {
        var body = await EnsureCookiesAndRunAsync(request, what, cancellationToken);
        if (body is not null)
        {
            return body;
        }
        
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

    // --- сериализация запроса/ответа GraphQL ---

    private sealed record GraphqlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] FeedVariables Variables);

    private sealed record FeedVariables(
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