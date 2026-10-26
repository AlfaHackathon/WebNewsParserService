using AlfaHackaton.Kafka.Producer.Interfaces;
using Api.Common;
using Api.Contracts;
using Api.Contracts.Kafka;
using Api.Models;
using Api.Models.Tass;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;
using Api.Workers;
using MongoDB.Bson;

namespace Api.Services;

/// <summary>Бизнес-операции над новостями: Mongo + Kafka (AlfaHackaton.Kafka).</summary>
public class NewsService(
    ITassClient tass,
    IAlfaClient alfa,
    IRbcClient rbc,
    INewsRepository news,
    IKafkaProducer<NewsEventMessage> kafka,
    ILogger<NewsService> logger,
    IConfiguration configuration) : INewsService
{
    private const int ProduceTimeoutSeconds = 10;

    public async Task PollTassAsync(CancellationToken cancellationToken = default)
    {
        var latest = await tass.GetLatestAsync(50, cancellationToken);
        if (latest is null)
        {
            logger.LogWarning("TASS недоступен, цикл парсинга пропущен");
            return;
        }

        foreach (var item in latest)
        {
            var externalId = item.ContentUrl.Split("/").Last();
            var newsResult = await news.GetByExternalIdAndSourceAsync(externalId, NewsSource.Tass, cancellationToken);

            if (newsResult is not null)
            {
                continue;
            }

            var doc = new NewsDocument
            {
                Source = NewsSource.Tass,
                Url = "https://tass.ru/" + item.ContentUrl,
                Title = item.Title,
                ExternalId = externalId,
                PublishedDt = DateTime.SpecifyKind(item.PublishedDt, DateTimeKind.Utc),
                StoredAt = DateTime.UtcNow
            };

            await news.AddAsync(doc, cancellationToken);
            TassNewsTextParserWorker.NewsStack.Push(doc);
            // _ = ProduceEventAsync(new NewsEventMessage(doc.Source, id.ToString()), cancellationToken);
        }
    }

    /// <summary>
    /// Цикл альфа: курсором проходим свежую часть ленты (дедупликация по uuid),
    /// свежие статьи открываем ради полного текста (лимит на цикл — Alfa:MaxArticleFetchPerPoll),
    /// о каждой новой шлём событие в Kafka.
    /// </summary>
    public async Task PollAlfaAsync(CancellationToken cancellationToken = default)
    {
        
        var feed = await alfa.GetFeedAsync(cancellationToken);
        foreach (var publication in feed.Publications)
        {
            if (string.IsNullOrEmpty(publication.Uuid))
            {
                continue;
            }

            // полный текст только у свежих (в ленте его нет): открытие страницы
            // статьи — секунды, поэтому ограничиваем число загрузок за цикл
            var article = await alfa.GetArticleAsync(publication.Path, cancellationToken);
            var text = article?.Body ?? string.Empty;

            var doc = new NewsDocument
            {
                Url = $"https://alfabank.ru/alfa-investor/t/{publication.Path}",
                Source = NewsSource.Alfa,
                ExternalId = publication.Path,
                Title = publication.Title,
                Text = text,
                PublishedDt = publication.PublishAt.ToUniversalTime(),
                StoredAt = DateTime.UtcNow
            };

            if (await news.GetByExternalIdAndSourceAsync(doc.ExternalId, NewsSource.Alfa, cancellationToken) is not null)
            {
                continue;
            }

            var id = await news.AddAsync(doc, cancellationToken);
            _ = ProduceEventAsync(new NewsEventMessage(NewsSource.Alfa, id.ToString()), cancellationToken);
        }
        
    }
    

    /// <summary>
    /// Цикл РБК Инвестиций: лента (materials + newsfeed из NEXT_DATA страницы /quote),
    /// свежие публикации (дедупликация по id) открываем ради полного текста,
    /// о каждой новой шлём событие в Kafka.
    /// </summary>
    public async Task PollRbcAsync(CancellationToken cancellationToken = default)
    {
        var feed = await rbc.GetFeedAsync(cancellationToken);
        if (feed is null)
        {
            logger.LogWarning("РБК Инвестиции недоступны, цикл парсинга пропущен");
            return;
        }

        foreach (var item in feed.Items)
        {
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Url))
            {
                continue;
            }

            // полный текст в ленте не отдаётся, открываем страницу материала
            var article = await rbc.GetArticleAsync(item.Url, cancellationToken);
            var text = article?.BodyMd ?? string.Empty;

            var doc = new NewsDocument
            {
                Url = item.Url,
                Source = NewsSource.Rbc,
                ExternalId = item.Id,
                Title = item.Headline,
                Text = text,
                PublishedDt = item.PublishedAt,
                StoredAt = DateTime.UtcNow
            };

            if (await news.GetByExternalIdAndSourceAsync(doc.ExternalId, NewsSource.Rbc, cancellationToken) is not null)
            {
                continue;
            }

            var id = await news.AddAsync(doc, cancellationToken);
            _ = ProduceEventAsync(new NewsEventMessage(NewsSource.Rbc, id.ToString()), cancellationToken);
        }
    }

    public async Task<List<NewsSearchResponse>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default)
    {
        var docs = await news.SearchAsync(search, limit, cancellationToken);
        return docs
            .Select(d => new NewsSearchResponse(d.Source, d.ExternalId, d.Title, d.Text, d.PublishedDt, SourceUrl(d.Url)))
            .ToList();
    }

    public async Task<NewsSearchResponse?> GetNewsById(string id, CancellationToken cancellationToken = default)
    {
        var result = await news.GetByIdAsync(ObjectId.Parse(id), cancellationToken);
        if (result is null)
        {
            return null;
        }

        return new NewsSearchResponse(
            result.Source,
            result.ExternalId,
            result.Title,
            result.Text,
            result.PublishedDt,
            SourceUrl(result.Url));
    }

    private static string? SourceUrl(string? value) => value is { Length: > 0 and <= 4096 } &&
        !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == '\\') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        uri.Host.Length > 0 && uri.UserInfo.Length == 0 ? value : null;

    public Task<List<TassSearchItem>?> SearchTassAsync(string search, int limit, CancellationToken cancellationToken = default)
        => tass.SearchAsync(search, limit, cancellationToken);

    /// <summary>
    /// Produce с ограничением по времени: лежащая на боку Kafka не должна блокировать парсинг
    /// (дефолтный message.timeout у produce — минуты; ждём ProduceTimeoutSeconds и не блокируем цикл,
    /// undelivered событий Confluent.Kafka доставит, когда брокер поднимется).
    /// </summary>
    private async Task ProduceEventAsync(NewsEventMessage message, CancellationToken cancellationToken)
    {
        var produce = kafka.ProduceAsync(configuration["Kafka:Topic"] ?? "news-parser.news", message, cancellationToken);
        if (await Task.WhenAny(produce, Task.Delay(TimeSpan.FromSeconds(ProduceTimeoutSeconds), cancellationToken)) != produce)
        {
            logger.LogWarning("Kafka недоступна — событие о новости {Source}/{Id} будет доставлено позже",
                message.Source, message.Id);
            return;
        }

        await produce;
    }
}
