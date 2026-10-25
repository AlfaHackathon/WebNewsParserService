using AlfaHackaton.Kafka.Producer.Interfaces;
using Api.Common;
using Api.Contracts;
using Api.Contracts.Kafka;
using Api.Models;
using Api.Models.Tass;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;
using Api.Settings;
using Api.Workers;

namespace Api.Services;

/// <summary>Бизнес-операции над новостями: Mongo + Kafka (AlfaHackaton.Kafka).</summary>
public class NewsService(
    ITassClient tass,
    IAlfaClient alfa,
    AlfaClientSettings alfaSettings,
    IRbcClient rbc,
    RbcClientSettings rbcSettings,
    INewsRepository news,
    IKafkaProducer<NewsEventMessage> kafka,
    ILogger<NewsService> logger) : INewsService
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

            var id = await news.AddAsync(doc, cancellationToken);
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
            // _ = ProduceEventAsync(new NewsEventMessage(NewsSource.Alfa, id.ToString()), cancellationToken);
            
        }
        
    }
    
    /// <summary>
    /// Цикл РБК Инвестиций: забираем ленту (materials + newsfeed из NEXT_DATA страницы
    /// /quote), свежие публикации (дедупликация по id) открываем ради полного текста
    /// (лимит на цикл — Rbc:MaxArticleFetchPerPoll), о каждой новой шлём событие в Kafka.
    /// </summary>
    // public async Task PollRbcAsync(CancellationToken cancellationToken = default)
    // {
    //     var feed = await rbc.GetFeedAsync(cancellationToken);
    //     if (feed is null)
    //     {
    //         // не падаем: РБК недоступен — просто пропускаем цикл
    //         logger.LogWarning("РБК Инвестиции недоступны, цикл парсинга пропущен");
    //         return;
    //     }
    //
    //     var fresh = 0;
    //     var articleFetched = 0;
    //     foreach (var item in feed.Items)
    //     {
    //         if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.Url))
    //         {
    //             continue;
    //         }
    //
    //         try
    //         {
    //             // полный текст в ленте не отдаётся — открытие страницы статьи условно
    //             // доли секунды×десятки, поэтому ограничиваем число загрузок за цикл
    //             var text = string.Empty;
    //             if (articleFetched < rbcSettings.MaxArticleFetchPerPoll)
    //             {
    //                 var article = await rbc.GetArticleAsync(item.Url, cancellationToken);
    //                 articleFetched++;
    //                 text = article?.BodyMd ?? string.Empty;
    //             }
    //
    //             var doc = new NewsDocument
    //             {
    //                 Source = NewsSource.Rbc,
    //                 ExternalId = item.Id,
    //                 Title = item.Headline,
    //                 Text = text,
    //                 PublishedDt = item.PublishedAt,
    //                 StoredAt = DateTime.UtcNow
    //             };
    //
    //             if (!await news.AddAsync(doc, cancellationToken))
    //             {
    //                 continue; // уже в базе
    //             }
    //
    //             fresh++;
    //             await ProduceEventAsync(new NewsEventMessage(
    //                 doc.Source, doc.ExternalId, doc.Title, doc.Text, doc.PublishedDt), cancellationToken);
    //         }
    //         catch (OperationCanceledException)
    //         {
    //             throw;
    //         }
    //         catch (Exception exception)
    //         {
    //             // одна сломанная новость не должна уронить весь цикл
    //             logger.LogError(exception, "Не удалось сохранить новость rbc/{ExternalId}", item.Id);
    //         }
    //     }
    //
    //     if (fresh > 0)
    //     {
    //         logger.LogInformation("Новых новостей rbc за цикл: {Count}", fresh);
    //     }
    // }

    public async Task<List<NewsSearchResponse>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default)
    {
        var docs = await news.SearchAsync(search, limit, cancellationToken);
        return docs
            .Select(d => new NewsSearchResponse(d.Source, d.ExternalId, d.Title, d.Text, d.PublishedDt))
            .ToList();
    }

    public Task<List<TassSearchItem>?> SearchTassAsync(string search, int limit, CancellationToken cancellationToken = default)
        => tass.SearchAsync(search, limit, cancellationToken);

    /// <summary>
    /// Produce с ограничением по времени: лежащая на боку Kafka не должна блокировать парсинг
    /// (дефолтный message.timeout у produce — минуты; ждём ProduceTimeoutSeconds и не блокируем цикл,
    /// undelivered событий Confluent.Kafka доставит, когда брокер поднимется).
    /// </summary>
    private async Task ProduceEventAsync(NewsEventMessage message, CancellationToken cancellationToken)
    {
        var produce = kafka.ProduceAsync(message, cancellationToken);
        if (await Task.WhenAny(produce, Task.Delay(TimeSpan.FromSeconds(ProduceTimeoutSeconds), cancellationToken)) != produce)
        {
            logger.LogWarning("Kafka недоступна — событие о новости {Source}/{Id} будет доставлено позже",
                message.Source, message.Id);
            return;
        }

        await produce;
    }
}