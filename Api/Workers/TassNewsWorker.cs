using Api.Services.Interfaces;

namespace Api.Workers;

/// <summary>
/// Воркер TASS: раз в N секунд (Worker:TassPollIntervalSeconds) смотрит ленту «главной»,
/// сохраняет новые новости в Монгу и шлёт уведомление в Kafka.
/// </summary>
public class TassNewsWorker(
    INewsService news,
    IConfiguration configuration,
    ILogger<TassNewsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue<int?>("Worker:TassPollIntervalSeconds") ?? 30;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        logger.LogInformation("Воркер TASS запущен, интервал {Seconds} с", seconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await news.PollTassAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // цикл не должен умирать разом: проблемы TASS/Mongo ретраятся в следующий тик
                logger.LogError(exception, "Цикл парсинга TASS упал, ждём следующий тик");
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

        logger.LogInformation("Воркер TASS остановлен");
    }
}
