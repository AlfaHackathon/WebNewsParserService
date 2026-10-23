using Api.Services.Interfaces;

namespace Api.Workers;


public class TassNewsWorker : BackgroundService
{
    private readonly INewsService _news;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TassNewsWorker> _logger;

    public TassNewsWorker(INewsService news,
        IConfiguration configuration,
        ILogger<TassNewsWorker> logger)
    {
        _news = news;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = _configuration.GetValue<int?>("Worker:TassPollIntervalSeconds") ?? 30;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        _logger.LogInformation("Воркер TASS запущен, интервал {Seconds} с", seconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _news.PollTassAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Цикл парсинга TASS упал, ждём следующий тик");
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

        _logger.LogInformation("Воркер TASS остановлен");
    }
}
