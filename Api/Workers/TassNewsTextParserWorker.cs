using System.Collections.Concurrent;
using AlfaHackaton.Kafka.Producer.Interfaces;
using Api.Common;
using Api.Contracts.Kafka;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services.Interfaces;

namespace Api.Workers;

public class TassNewsTextParserWorker : BackgroundService
{
    public static ConcurrentStack<NewsDocument> NewsStack = new();
    private readonly INewsRepository _newsRepository;
    private readonly ITassWebDriverService _driverService;
    private readonly ILogger<TassNewsWorker> _logger;
    private readonly IKafkaProducer<NewsEventMessage> _kafka;
    private readonly IConfiguration _configuration;


    public TassNewsTextParserWorker(ITassWebDriverService driverService, INewsRepository newsRepository, ILogger<TassNewsWorker> logger, IKafkaProducer<NewsEventMessage> kafka, IConfiguration configuration)
    {
        _driverService = driverService;
        _newsRepository = newsRepository;
        _logger = logger;
        _kafka = kafka;
        _configuration = configuration;
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

            try
            {
                document.Text = _driverService.GetTextNews(document.Url);
                await _newsRepository.ReplaceAsync(document, stoppingToken);
                _ = _kafka.ProduceAsync(_configuration["Kafka:Topic"] ?? "news-parser.news", new NewsEventMessage(NewsSource.Tass, document.Id.ToString()), stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Не получилось спарсить текст новости {documentUrl} в ТАСС", document.Url);
            }

            Thread.Sleep(3000);
        }
    }
}