namespace Api.Contracts.Kafka;

/// <summary>
/// Событие «спаршена новая новость», улетает в Kafka топик news-parser.news.
/// Поля = то же самое, что лежит в Монге.
/// </summary>
public record NewsEventMessage(
    string Source,
    string ExternalId,
    string Title,
    string Text,
    DateTime PublishedAt);
