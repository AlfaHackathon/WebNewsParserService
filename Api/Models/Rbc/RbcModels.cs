using System.Text.Json.Serialization;

namespace Api.Models.Rbc;

/// <summary>
/// Корень __NEXT_DATA__ страницы rbc.ru/quote (Next.js SSR-состояние).
/// Интересное — в props.pageProps: materials (материалы Инвестиций) и newsfeed (живая лента).
/// </summary>
public class RbcNextData
{
    [JsonPropertyName("props")]
    public RbcProps? Props { get; set; }
}

public class RbcProps
{
    [JsonPropertyName("pageProps")]
    public RbcPageProps? PageProps { get; set; }
}

public class RbcPageProps
{
    /// <summary>Главные материалы «РБК Инвестиций» (с тикерами).</summary>
    [JsonPropertyName("materials")]
    public RbcItemsBlock? Materials { get; set; }

    /// <summary>Живая лента новостей (та самая вкладка #feed).</summary>
    [JsonPropertyName("newsfeed")]
    public RbcItemsBlock? Newsfeed { get; set; }

    /// <summary>Материал текущей страницы (только на страницах статей — там полный текст).</summary>
    [JsonPropertyName("material")]
    public RbcArticle? Material { get; set; }
}

/// <summary>Блок списка публикаций: элементы + признак есть-ли-ещё.</summary>
public class RbcItemsBlock
{
    public List<RbcFeedItem> Items { get; set; } = [];

    [JsonPropertyName("moreExists")]
    public bool MoreExists { get; set; }

    [JsonPropertyName("endCursor")]
    public string? EndCursor { get; set; }
}

/// <summary>
/// Публикация из ленты/материалов РБК. Полного текста тут нет —
/// он берётся со страницы статьи (см. RbcClient.GetArticleAsync).
/// </summary>
public class RbcFeedItem
{
    /// <summary>Заголовок (у разных типов бывает в Header).</summary>
    public string? Header { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Идентификатор новости — он же хвост URL статьи.</summary>
    public string? Id { get; set; }

    /// <summary>Тип: article / short_news / photoslider ...</summary>
    public string? Type { get; set; }

    /// <summary>Абсолютный URL публикации.</summary>
    public string? Url { get; set; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; set; }

    /// <summary>Дата публикации в unix-времени.</summary>
    [JsonPropertyName("publishDateT")]
    public long? PublishDateT { get; set; }

    /// <summary>Краткий анонс (у материалов полноценно заполнен, у новостей часто пуст).</summary>
    [JsonPropertyName("anons")]
    public string? Anons { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    /// <summary>Проект: rbcnews / quote / ...</summary>
    [JsonPropertyName("projectNick")]
    public string? ProjectNick { get; set; }

    /// <summary>Рубрика (title — «Политика», «Инвестиции»...).</summary>
    public RbcRubric? Rubric { get; set; }

    /// <summary>Привязанные биржевые активы (акции, индексы, валюты).</summary>
    public List<RbcTicker> Tickers { get; set; } = [];

    /// <summary>Готовое читаемое имя: Header, на только HeYDN-объекты бывают только title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Headline => (Header ?? Title) ?? string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTime PublishedAt =>
        PublishDateT is { } t && t != 0
            ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime
            : DateTime.TryParse(PublishDate, out var parsed) ? parsed : DateTime.MinValue;
}

/// <summary>Рубрика публикации.</summary>
public class RbcRubric
{
    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary>Актив из tickers: акция (SBER), индекс (IMOEX), валюта (USD)...</summary>
public class RbcTicker
{
    public string? Title { get; set; }

    /// <summary>currency / index / share / bond ...</summary>
    public string? Type { get; set; }

    /// <summary>Числовое id эмитента (emitbase).</summary>
    [JsonPropertyName("emitbaseId")]
    public long? EmitbaseId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>
/// Полная статья со страницы публикации (props.pageProps.material).
/// BodyMd — markdown со всеми абзацами, таблицами и вставками.
/// </summary>
public class RbcArticle
{
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Полный текст статьи (markdown).</summary>
    [JsonPropertyName("bodyMd")]
    public string? BodyMd { get; set; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; set; }

    [JsonPropertyName("publishDateT")]
    public long? PublishDateT { get; set; }

    [JsonPropertyName("anons")]
    public string? Anons { get; set; }

    public List<RbcTicker> Tickers { get; set; } = [];
}
