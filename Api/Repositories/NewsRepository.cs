using System.Text.RegularExpressions;
using AlfaHackaton.MongoContext;
using Api.Common;
using Api.Models;
using Api.Repositories.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Api.Repositories;

public class NewsRepository(IMongoContext mongo) : INewsRepository
{
    private const string CollectionName = "news";
    private IMongoCollection<NewsDocument> Collection => mongo.GetCollection<NewsDocument>(CollectionName);

    public async Task<ObjectId> AddAsync(NewsDocument news, CancellationToken cancellationToken = default)
    {
        await Collection.InsertOneAsync(news, cancellationToken: cancellationToken);
        return news.Id;
    }

    public Task<NewsDocument?> GetByExternalIdAndSourceAsync(string externalId, NewsSource source, CancellationToken cancellationToken = default)
    {
        var filter = Builders<NewsDocument>.Filter.And(
            Builders<NewsDocument>.Filter.Eq(n => n.Source, source),
            Builders<NewsDocument>.Filter.Eq(n => n.ExternalId, externalId));

        return Collection.Find(filter).FirstOrDefaultAsync(cancellationToken)!;
    }

    public async Task<bool> ReplaceAsync(NewsDocument news, CancellationToken cancellationToken = default)
    {
        var filter = Builders<NewsDocument>.Filter.Eq(n => n.Id, news.Id);
        var result = await Collection.ReplaceOneAsync(filter, news, cancellationToken: cancellationToken);
        return result.IsAcknowledged && result.MatchedCount > 0;
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