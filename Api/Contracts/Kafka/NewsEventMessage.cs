using Api.Common;

namespace Api.Contracts.Kafka;

/// <summary>
/// Событие «спаршена новая новость», улетает в Kafka топик news-parser.news.
/// Поля = то же самое, что лежит в Монге.
/// </summary>
public record NewsEventMessage(
    NewsSource Source,
    string Id);
