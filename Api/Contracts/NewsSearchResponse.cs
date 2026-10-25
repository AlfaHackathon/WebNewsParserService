using Api.Common;

namespace Api.Contracts;

/// <summary>Новость из Монги (результат internal-поиска).</summary>
public record NewsSearchResponse(
    NewsSource Source,
    string ExternalId,
    string Title,
    string Text,
    DateTime PublishedAt);
