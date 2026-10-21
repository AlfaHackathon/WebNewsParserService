using System.Text.RegularExpressions;
using AlfaHackaton.MongoContext;
using Api.Models;
using Api.Repositories.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Api.Repositories;

public class NewsRepository(IMongoContext mongo) : INewsRepository
{
    private const string CollectionName = "news";
    private IMongoCollection<NewsDocument> Collection => mongo.GetCollection<NewsDocument>(CollectionName);

    public async Task<bool> TryAddAsync(NewsDocument news, CancellationToken cancellationToken = default)
    {
        // Дедупликация проверкой перед вставкой (индекс (Source, ExternalId) уникальный и ловит гонки)
        var duplicate = Builders<NewsDocument>.Filter.And(
            Builders<NewsDocument>.Filter.Eq(n => n.Source, news.Source),
            Builders<NewsDocument>.Filter.Eq(n => n.ExternalId, news.ExternalId));

        if (await Collection.Find(duplicate).AnyAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            await Collection.InsertOneAsync(news, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError.Code == 11000)
        {
            // 11000 — duplicate key: гонка, новость вставил другой воркер между проверкой и вставкой
            return false;
        }
    }

    public async Task<List<NewsDocument>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default)
    {
        // Case-insensitive подстрока: текстовый индекс Mongo ищет слова, а подстрока удобнее для коротких запросов
        var pattern = new BsonRegularExpression(Regex.Escape(search), "i");
        var filter = Builders<NewsDocument>.Filter.Or(
            Builders<NewsDocument>.Filter.Regex(n => n.Title, pattern),
            Builders<NewsDocument>.Filter.Regex(n => n.Text, pattern));

        return await Collection.Find(filter)
            .SortByDescending(n => n.PublishedDt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }
}
