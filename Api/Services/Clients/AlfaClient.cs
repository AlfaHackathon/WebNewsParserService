using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AlfaHackaton.HttpClient.Common.Interfaces;
using AlfaHackaton.HttpClient.Common.Models;
using Api.Models.Alfa;
using Api.Services.Interfaces;
using Api.Settings;

namespace Api.Services.Clients;

/// <summary>
/// Клиент «Альфа-Инвестора»: ходим через AlfaHackaton.HttpClient,
/// заголовки — как у их же фронтенда (см. AlfaClientSettings.DefaultHeaders).
/// </summary>
public class AlfaClient(
    IHttpClient<AlfaClientSettings> http,
    AlfaClientSettings settings,
    ILogger<AlfaClient> logger) : IAlfaClient
{
    /// <summary>Маркер заглушки антибот-челленджа в теле ответа (обрывок их обёртки-спиннера).</summary>
    private const string ChallengeMarker = "js-challenge-loader";

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    public async Task<AlfaFeed?> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        // shape переменных — 1:1 как шлёт их фронтенд (см. пагинацию «Показать ещё»)
        var request = new GraphqlRequest(
            Resources.GraphQLQueriesAlfa.FeedQuery,
            new FeedVariables(
                new RubricCodesFilter([settings.Rubric]),
                new FilterTypeFilter("rubrics")));

        var response = await http.Post("/api/v1/eye-of-investor/graphql", request, cancellationToken: cancellationToken);
        if (!IsOk(response, "лента"))
        {
            return null;
        }

        var feed = JsonSerializer.Deserialize<AlfaFeedResponse>(response.TextData, JsonWeb)?.Data?.Feed;
        if (feed is null)
        {
            logger.LogWarning("Альфа лента: в ответе нет getFeedPublications");
        }

        return feed;
    }

    public async Task<AlfaArticle?> GetArticleAsync(string path, CancellationToken cancellationToken = default)
    {
        var articleUrl = $"/alfa-investor/t/{path.Trim('/')}/";

        var response = await http.Get(articleUrl,
            (headers, _) => headers.Accept.ParseAdd("text/html"), cancellationToken);
        if (!IsOk(response, $"статья {path}"))
        {
            return null;
        }

        var articleJson = ExtractArticleJson(response.TextData);
        if (articleJson is null || articleJson.Value.ValueKind == JsonValueKind.Null)
        {
            logger.LogWarning("В SSR-состоянии альфа-статьи нет объекта article ({Url})", articleUrl);
            return null;
        }

        return JsonSerializer.Deserialize<AlfaArticle>(articleJson.Value, JsonWeb);
    }

    /// <summary>Статус + маркер антибот-челленджа в теле. false — запрос не удался, у вызывающего null.</summary>
    private bool IsOk(Response response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Альфа {What}: статус {Status}", what, (int)response.Status);
            return false;
        }

        if (string.IsNullOrEmpty(response.TextData))
        {
            logger.LogWarning("Альфа {What}: пустой ответ", what);
            return false;
        }

        if (response.TextData.Contains(ChallengeMarker, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Альфа {What}: ответ — антибот-челлендж", what);
            return false;
        }

        return true;
    }


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
}
