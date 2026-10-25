using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Models.Rbc;
using Api.Services.Interfaces;
using Api.Settings;

namespace Api.Services.Clients;

/// <summary>
/// Клиент «РБК Инвестиций» (https://www.rbc.ru/quote#feed), простой HttpClient.
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
    RbcClientSettings settings,
    HttpClient http,
    ILogger<RbcClient> logger) : IRbcClient
{
    private static readonly Regex NextDataRegex = new(
        "<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    public async Task<RbcItemsBlock?> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        var html = await GetStringAsync(settings.FeedPagePath, "лента", cancellationToken);
        if (html is null)
        {
            return null;
        }

        var nextData = ExtractNextData<RbcNextData>(html);
        var props = nextData?.Props?.PageProps;
        if (props is null)
        {
            logger.LogWarning("РБК: в HTML ленты нет NEXT_DATA (pageProps)");
            return null;
        }

        // материалы Инвестиций содержательнее (полные анонсы + тикеры), поэтому
        // они основные; newsfeed — живая лента, идёт вторым списком
        var materials = props.Materials ?? props.Newsfeed;
        if (props.Newsfeed is { Items.Count: > 0 } newsfeed)
        {
            // newsfeed частично дублирует материалы — мержим без повторов по id
            var known = materials is { Items.Count: > 0 }
                ? materials.Items.Select(i => i.Id).ToHashSet()
                : [];

            var fresh = newsfeed.Items.Where(i => !string.IsNullOrEmpty(i.Id) && !known.Contains(i.Id));
            if (materials is null)
            {
                materials = newsfeed;
            }
            else
            {
                materials.Items.AddRange(fresh);
            }
        }

        return materials;
    }

    public async Task<RbcArticle?> GetArticleAsync(string url, CancellationToken cancellationToken = default)
    {
        var html = await GetStringAsync(url, "статья", cancellationToken);
        if (html is null)
        {
            return null;
        }

        var nextData = ExtractNextData<RbcNextData>(html);
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

    private async Task<string?> GetStringAsync(string path, string what, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("РБК {What}: статус {Status}", what, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "РБК {What}: сетевая ошибка", what);
            return null;
        }
    }
}
