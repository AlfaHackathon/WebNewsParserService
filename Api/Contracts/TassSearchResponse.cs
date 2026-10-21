namespace Api.Contracts;

/// <summary>Результат proxy-поиска TASS (без сохранения в Монгу).</summary>
public record TassSearchResponse(
    string ExternalId,
    string Title,
    string? Lead,
    string Url,
    DateTime PublishedAt);
