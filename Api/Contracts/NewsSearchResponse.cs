namespace Api.Contracts;

/// <summary>Новость из Монги (результат internal-поиска).</summary>
public record NewsSearchResponse(
    string Source,
    string ExternalId,
    string Title,
    string Text,
    DateTime PublishedAt);
