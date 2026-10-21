using Api.Services.Interfaces;

namespace Api.Workers;

/// <summary>
/// Воркер «Альфа-Инвестора»: раз в N секунд (Worker:AlfaPollIntervalSeconds, по умолчанию 300)
/// смотрит ленту новостей через их внутренний GraphQL, сохраняет новое в Монгу
/// и шлёт уведомление в Kafka.
/// </summary>
public class AlfaNewsWorker(
    INewsService news,
    IConfiguration configuration,
    ILogger<AlfaNewsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue<int?>("Worker:AlfaPollIntervalSeconds") ?? 300;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        logger.LogInformation("Воркер Альфа запущен, интервал {Seconds} с", seconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await news.PollAlfaAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // цикл не должен умирать разом: проблемы альфы/Mongo ретраятся в следующий тик
                logger.LogError(exception, "Цикл парсинга альфа упал, ждём следующий тик");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Воркер Альфа остановлен");
    }
}
