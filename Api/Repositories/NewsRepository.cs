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
            return false;
        }
    }

    public Task<NewsDocument?> GetByExternalIdAsync(string externalId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<NewsDocument>.Filter.Eq(n => n.ExternalId, externalId);
        return Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Дозаписать полный текст у уже сохранённой новости.</summary>
    public async Task<bool> UpdateTextAsync(string source, string externalId, string text, CancellationToken cancellationToken = default)
    {
        var filter = Builders<NewsDocument>.Filter.And(
            Builders<NewsDocument>.Filter.Eq(n => n.Source, source),
            Builders<NewsDocument>.Filter.Eq(n => n.ExternalId, externalId));

        var update = Builders<NewsDocument>.Update.Set(n => n.Text, text);
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }

    public async Task<List<NewsDocument>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default)
    {
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
