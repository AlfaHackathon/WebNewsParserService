using Api.Services.Interfaces;

namespace Api.Workers;

/// <summary>
/// Воркер «РБК Инвестиций»: раз в N секунд (Worker:RbcPollIntervalSeconds, по умолчанию 300)
/// смотрит ленту quote (NEXT_DATA страницы), сохраняет новое в Монгу
/// и шлёт уведомление в Kafka.
/// </summary>
public class RbcNewsWorker(
    INewsService news,
    IConfiguration configuration,
    ILogger<RbcNewsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue<int?>("Worker:RbcPollIntervalSeconds") ?? 300;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        logger.LogInformation("Воркер РБК запущен, интервал {Seconds} с", seconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await news.PollRbcAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // цикл не должен умирать разом: проблемы РБК/Mongo ретраятся в следующий тик
                logger.LogError(exception, "Цикл парсинга РБК упал, ждём следующий тик");
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

        logger.LogInformation("Воркер РБК остановлен");
    }
}
