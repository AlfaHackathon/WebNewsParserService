using AlfaHackaton.HttpClient;
using AlfaHackaton.Kafka;
using AlfaHackaton.MongoContext;
using Api.Common;
using Api.Contracts.Kafka;
using Api.Repositories;
using Api.Repositories.Interfaces;
using Api.Services;
using Api.Services.Interfaces;
using Api.Settings;
using Api.Workers;
using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Mongo (AlfaHackaton.MongoContext): одна база на сервис — news-parser (см. agent.md)
builder.Services.AddMongo(new MongoSettings(
    builder.Configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017",
    builder.Configuration["Mongo:Database"] ?? "news-parser"));

// Индексы news: уникальный (Source, ExternalId) — дедупликация; текстовый по заголовку и тексту
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

// Клиент tass.ru через AlfaHackaton.HttpClient: BaseAddress — в TassClientSettings
builder.Services.AddAlfaHttpClient(new TassClientSettings(builder.Configuration));

// Kafka: IKafkaProducer<NewsEventMessage> — уведомления «новая новость» в топик news-parser.news
builder.Services.AddProducer<NewsEventMessage>(builder.Configuration.GetSection("Kafka"));

// Слой бизнес-логики: контроллеры и воркер работают только с сервисом.
// Singleton: воркер (hosted service) тоже тянет эти зависимости
builder.Services.AddSingleton<INewsService, NewsService>();
builder.Services.AddSingleton<ITassClient, TassClient>();

// Воркер-парсер TASS
builder.Services.AddHostedService<TassNewsWorker>();

// Префикс маршрутов: /news-parser-service/...
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new RoutePrefixConvention("news-parser-service"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Создаём Mongo-индексы при старте (идемпотентно)
await app.Services.UseMongoIndexesAsync();

// Swagger: доступен на /news-parser-service/swagger, включается флагом Swagger:Enabled
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
