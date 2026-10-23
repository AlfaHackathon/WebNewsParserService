using System.Collections.Concurrent;
using Api.Models;
using Api.Services.Interfaces;

namespace Api.Workers;

public class TassNewsTextParserWorker : BackgroundService
{
    public static ConcurrentStack<NewsDocument> NewsStack = new();
    private readonly ITassWebDriverService _driverService;

    public TassNewsTextParserWorker(ITassWebDriverService driverService)
    {
        _driverService = driverService;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!NewsStack.TryPop(out var document))
            {
                Thread.Sleep(1000);
                continue;
            }

            document.Text = _driverService.GetTextNews(document.Url);
            Thread.Sleep(3000);
        }

        return Task.CompletedTask;
    }
}