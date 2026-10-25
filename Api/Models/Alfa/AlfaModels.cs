using System.Text.Json.Serialization;

namespace Api.Models.Alfa;

/// <summary>Ответ GraphQL-эндпоинта альфы: { data: { getFeedPublications {...} } }.</summary>
public class AlfaFeedResponse
{
    [JsonPropertyName("data")]
    public AlfaFeedData? Data { get; set; }
}

public class AlfaFeedData
{
    [JsonPropertyName("getFeedPublications")]
    public AlfaFeed? Feed { get; set; }
}

/// <summary>Одна страница ленты: пагинация курсором + список публикаций.</summary>
public class AlfaFeed
{
    public AlfaPagination? Pagination { get; set; }

    public List<AlfaPublication> Publications { get; set; } = [];
}

public class AlfaPagination
{
    public string? NextCursor { get; set; }

    public bool HasNextPage { get; set; }
}

/// <summary>
/// Публикация из ленты Альфа-Инвестора. Текст статьи в ленте не отдаётся —
/// полный текст берём со страницы статьи (см. AlfaClient.GetArticleAsync).
/// </summary>
public class AlfaPublication
{
    public string Uuid { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Слаг статьи: /alfa-investor/t/&lt;path&gt;/.</summary>
    public string Path { get; set; } = string.Empty;

    public DateTime PublishAt { get; set; }

    public AlfaAuthor? Author { get; set; }

    public AlfaRubric? Rubric { get; set; }

    public List<AlfaAsset> Assets { get; set; } = [];
}

public class AlfaAuthor
{
    public string Uuid { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
}

public class AlfaRubric
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

/// <summary>Актив, к которому привязана новость (акция etc.).</summary>
public class AlfaAsset
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ISIN")]
    public string Isin { get; set; } = string.Empty;

    public string Ticker { get; set; } = string.Empty;
}

/// <summary>
/// Полная статья, вытащенная из вшитого SSR-состояния страницы
/// (script#app_state -> pageContent.fetchResults[].response.value.article).
/// Body — markdown со всеми абзацами.
/// </summary>
public class AlfaArticle
{
    public string Uuid { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>Полный текст статьи (markdown).</summary>
    public string Body { get; set; } = string.Empty;

    public DateTime PublishAt { get; set; }

    public AlfaAuthor? Author { get; set; }

    public List<AlfaAsset> Assets { get; set; } = [];
}
