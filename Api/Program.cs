using AlfaHackaton.HttpClient;
using AlfaHackaton.Kafka;
using AlfaHackaton.MongoContext;
using Api.Common;
using Api.Contracts.Kafka;
using Api.Models;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Api.Services;
using Api.Services.Clients;
using Api.Services.Interfaces;
using Api.Settings;
using Api.Workers;
using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMongo(new MongoSettings(
    builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017",
    builder.Configuration["Mongo:Database"] ?? "news-parser"));

builder.Services.AddSingleton(new MongoIndexDefinition("news", async context =>
{
    var collection = context.Database.GetCollection<BsonDocument>("news");
    await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
        Builders<BsonDocument>.IndexKeys.Ascending("Source").Ascending("ExternalId"),
        new CreateIndexOptions { Unique = true }));
}));
builder.Services.AddSingleton(new MongoIndexDefinition("news", async context =>
{
    var collection = context.Database.GetCollection<BsonDocument>("news");
    await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
        Builders<BsonDocument>.IndexKeys.Text("Title").Text("Text")));
}));

builder.Services.AddSingleton<INewsRepository, NewsRepository>();

// Клиент альфы через AlfaHackaton.HttpClient:
var alfaSettings = new AlfaClientSettings(builder.Configuration);
builder.Services.AddSingleton(alfaSettings);
builder.Services.AddAlfaHttpClient(alfaSettings);
builder.Services.AddSingleton<IAlfaClient, AlfaClient>();

// Клиент tass-API через AlfaHackaton.HttpClient:
builder.Services.AddAlfaHttpClient(new TassClientSettings(builder.Configuration));

// Клиент «РБК Инвестиций» через AlfaHackaton.HttpClient:
// сайт отдаёт gzip/br-поток — без авто-распаковки тело приходит пустым
// (октет-стрим), поэтому декомпрессию включаем явно.
var rbcSettings = new RbcClientSettings(builder.Configuration);
builder.Services.AddSingleton(rbcSettings);
builder.Services.AddAlfaHttpClient(rbcSettings)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip
            | System.Net.DecompressionMethods.Brotli
            | System.Net.DecompressionMethods.Deflate
    });
builder.Services.AddSingleton<IRbcClient, RbcClient>();

builder.Services.AddProducer<NewsEventMessage>(builder.Configuration.GetSection("Kafka"));

builder.Services.AddSingleton<INewsService, NewsService>();
builder.Services.AddSingleton<ITassClient, TassClient>();
builder.Services.AddSingleton<ITassWebDriverService, TassWebDriverService>();
builder.Services.AddHostedService<TassNewsWorker>();
builder.Services.AddHostedService<TassNewsTextParserWorker>();

builder.Services.AddHostedService<AlfaNewsWorker>();
builder.Services.AddHostedService<RbcNewsWorker>();

builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new RoutePrefixConvention("news-parser-service"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await app.Services.UseMongoIndexesAsync();

if (app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger(options => options.RouteTemplate = "news-parser-service/swagger/{documentName}/swagger.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/news-parser-service/swagger/v1/swagger.json", "Web News Parser Service API");
        options.RoutePrefix = "news-parser-service/swagger";
    });
}

app.MapControllers();

app.Run();
