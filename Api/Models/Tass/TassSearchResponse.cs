using Newtonsoft.Json;

namespace Api.Models.Tass;

/// <summary>Ответ TASS /tbp/api/v1/search (поиск по сайту).</summary>
public class TassSearchResponse
{
    [JsonProperty("result")]
    public TassSearchResultBox? Result { get; set; }
}

public class TassSearchResultBox
{
    [JsonProperty("content_count")]
    public int ContentCount { get; set; }

    [JsonProperty("has_more")]
    public bool HasMore { get; set; }

    [JsonProperty("contents")]
    public List<TassSearchItem> Contents { get; set; } = [];
}

/// <summary>Элемент поиска TASS (нужны только внешние поля: id/title/lead/url/дата).</summary>
public class TassSearchItem
{
    [JsonProperty("composite_id")]
    public string CompositeId { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("lead")]
    public string? Lead { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;

    [JsonProperty("published_dt")]
    public DateTime PublishedDt { get; set; }
}
