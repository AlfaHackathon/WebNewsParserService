using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaHackaton.HttpClient.Common.Interfaces;
using AlfaHackaton.HttpClient.Common.Models;
using Api.Models.Rbc;
using Api.Services.Interfaces;
using Api.Settings;

namespace Api.Services.Clients;

/// <summary>
/// Клиент «РБК Инвестиций» (www.rbc.ru/quote#feed) через AlfaHackaton.HttpClient.
///
/// Публичного JSON-API у сайта нет, но это Next.js-приложение: его фронтенд
/// рендерится из состояния, вшитого в HTML (&lt;script id="__NEXT_DATA__"&gt;):
/// - лента: props.pageProps.materials (20 материалов Инвестиций с тикерами)
///          + props.pageProps.newsfeed (20 живых новостей);
/// - статья: props.pageProps.material.bodyMd — полный текст в markdown.
///
/// Так же, как делает их фронт: тянем HTML, вынимаем содержимое NEXT_DATA.
/// </summary>
public class RbcClient(
    IHttpClient<RbcClientSettings> http,
    RbcClientSettings settings,
    ILogger<RbcClient> logger) : IRbcClient
{
    private static readonly Regex NextDataRegex = new(
        "<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    public async Task<RbcItemsBlock?> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.Get(settings.FeedPagePath,
            (headers, _) => headers.Accept.ParseAdd("text/html"), cancellationToken);
        if (!IsOk(response, "лента"))
        {
            return null;
        }

        var nextData = ExtractNextData<RbcNextData>(response.TextData);
        var props = nextData?.Props?.PageProps;
        if (props is null)
        {
            logger.LogWarning("РБК: в HTML ленты нет NEXT_DATA (pageProps)");
            return null;
        }

        // материалы Инвестиций содержательнее (полные анонсы + тикеры), поэтому
        // они основные; newsfeed — живая лента, идёт вторым списком
        var feed = props.Materials ?? props.Newsfeed;
        if (props.Newsfeed is { Items.Count: > 0 } newsfeed)
        {
            // newsfeed частично дублирует материалы — мержим без повторов по id
            var known = feed is { Items.Count: > 0 }
                ? feed.Items.Select(i => i.Id).ToHashSet()
                : [];

            var fresh = newsfeed.Items.Where(i => !string.IsNullOrEmpty(i.Id) && !known.Contains(i.Id));
            if (feed is null)
            {
                feed = newsfeed;
            }
            else
            {
                feed.Items.AddRange(fresh);
            }
        }

        return feed;
    }

    public async Task<RbcArticle?> GetArticleAsync(string url, CancellationToken cancellationToken = default)
    {
        var response = await http.Get(url,
            (headers, _) => headers.Accept.ParseAdd("text/html"), cancellationToken);
        if (!IsOk(response, $"статья {url}"))
        {
            return null;
        }

        var nextData = ExtractNextData<RbcNextData>(response.TextData);
        var material = nextData?.Props?.PageProps?.Material;
        if (material is null || string.IsNullOrEmpty(material.BodyMd))
        {
            logger.LogWarning("РБК: в SSR-состоянии статьи нет bodyMd ({Url})", url);
            return null;
        }

        return material;
    }

    /// <summary>Достать __NEXT_DATA__ из HTML и десериализовать в модель.</summary>
    private T? ExtractNextData<T>(string html)
    {
        var match = NextDataRegex.Match(html);
        if (!match.Success)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(match.Groups[1].Value, JsonWeb);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "РБК: не удалось разобрать __NEXT_DATA__");
            return default;
        }
    }

    /// <summary>Статус + непустое тело. false — запрос не удался, у вызывающего null.</summary>
    private bool IsOk(Response response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("РБК {What}: статус {Status}", what, (int)response.Status);
            return false;
        }

        if (string.IsNullOrEmpty(response.TextData))
        {
            logger.LogWarning("РБК {What}: пустой ответ", what);
            return false;
        }

        return true;
    }
}
