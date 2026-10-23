using AlfaHackaton.Kafka.Producer.Interfaces;
using Api.Contracts;
using Api.Contracts.Kafka;
using Api.Models;
using Api.Models.Tass;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;
using Api.Workers;

namespace Api.Services;

/// <summary>Бизнес-операции над новостями: Mongo + Kafka (AlfaHackaton.Kafka).</summary>
public class NewsService(
    ITassClient tass,
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
            var doc = new NewsDocument
            {
                Source = "tass",
                Url = "https://tass.ru/" + item.ContentUrl,
                Title = item.Title,
                PublishedDt = DateTime.SpecifyKind(item.PublishedDt, DateTimeKind.Utc),
                StoredAt = DateTime.UtcNow
            };

            try
            {
                if (!await news.TryAddAsync(doc, cancellationToken))
                {
                    continue;
                }
                
                TassNewsTextParserWorker.NewsStack.Push(doc);
                // await ProduceEventAsync(new NewsEventMessage(
                //     doc.Source, doc.ExternalId, doc.Title, doc.Text, doc.PublishedDt), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось сохранить новость tass/{ExternalId}", doc.ExternalId);
            }
        }
    }

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
            logger.LogWarning("Kafka недоступна — событие о новости {Source}/{ExternalId} будет доставлено позже",
                message.Source, message.ExternalId);
            return;
        }

        await produce;
    }
}
