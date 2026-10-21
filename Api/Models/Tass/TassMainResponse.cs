using Newtonsoft.Json;

namespace Api.Models.Tass;

/// <summary>Ответ TASS /tgap/api/v1/messages/search (лента «главной», плоские элементы).</summary>
public class TassMainResponse
{
    [JsonProperty("result")]
    public List<TassMainItem> Result { get; set; } = [];
}

/// <summary>Элемент ленты «главной» TASS.</summary>
public class TassMainItem
{
    [JsonProperty("composite_id")]
    public string CompositeId { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("is_flash")]
    public bool IsFlash { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("subtitle")]
    public string? Subtitle { get; set; }

    [JsonProperty("published_dt")]
    public DateTime PublishedDt { get; set; }

    [JsonProperty("content_url")]
    public string ContentUrl { get; set; } = string.Empty;
}
