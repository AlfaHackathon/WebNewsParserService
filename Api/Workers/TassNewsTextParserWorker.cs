using System.Collections.Concurrent;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Workers;

public class TassNewsTextParserWorker : BackgroundService
{
    public static ConcurrentStack<NewsDocument> NewsStack = new();
    private readonly INewsRepository _newsRepository;
    private readonly ITassWebDriverService _driverService;

    public TassNewsTextParserWorker(ITassWebDriverService driverService, INewsRepository newsRepository)
    {
        _driverService = driverService;
        _newsRepository = newsRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!NewsStack.TryPop(out var document))
            {
                Thread.Sleep(1000);
                continue;
            }

            document.Text = _driverService.GetTextNews(document.Url);
            await _newsRepository.ReplaceAsync(document, stoppingToken);
            Thread.Sleep(3000);
        }
    }
}