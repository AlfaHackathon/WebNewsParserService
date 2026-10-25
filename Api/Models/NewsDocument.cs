using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Api.Models;

/// <summary>
/// Новость в Монге. Ничего лишнего: источник, идентификатор у источника,
/// заголовок, текст (lead) и даты.
/// </summary>
public class NewsDocument
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();

    /// <summary>Источник: "tass", "rbc", "alfa" (для следующих воркеров).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Идентификатор новости у источника (composite_id у TASS, uuid у Альфы, id у РБК).</summary>
    public string ExternalId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>Текст новости: полный текст со страницы материала, либо lead из ленты.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Дата публикации у источника.</summary>
    public DateTime PublishedDt { get; set; }

    /// <summary>Когда мы её спарсили и сохранили.</summary>
    public DateTime StoredAt { get; set; }
}
