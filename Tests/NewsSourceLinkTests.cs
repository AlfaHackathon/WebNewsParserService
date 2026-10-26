using System.Text.Json;
using Api.Common;
using Api.Models;
using Api.Repositories.Interfaces;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Xunit;

public sealed class NewsSourceLinkTests
{
    [Theory]
    [InlineData("https://publisher.example.test/news/123?from=feed#original", true)]
    [InlineData("http://publisher.example.test/news/123", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,unsafe", false)]
    [InlineData("file:///private", false)]
    [InlineData("/news/123", false)]
    [InlineData("https://user:password@publisher.example.test/news", false)]
    [InlineData(" https://publisher.example.test/news", false)]
    [InlineData("https://publisher.example.test/ne\nws", false)]
    [InlineData("https://publisher.example.test/a\\b", false)]
    public async Task GetAndSearchProjectOnlyTheStoredSafeUrl(string? url, bool safe)
    {
        var document = new NewsDocument
        {
            Source = NewsSource.Tass, ExternalId = "publisher-123", Title = "Synthetic publisher announcement",
            Text = "Synthetic lead", Url = url!, PublishedDt = DateTime.SpecifyKind(new(2026, 10, 24, 12, 0, 0), DateTimeKind.Utc)
        };
        var service = new NewsService(null!, null!, null!, new Repository(document), null!,
            NullLogger<NewsService>.Instance, new ConfigurationBuilder().Build());
        var detail = await service.GetNewsById(document.Id.ToString());
        var search = Assert.Single(await service.SearchAsync("Synthetic", 5));
        foreach (var dto in new[] { detail!, search })
        {
            Assert.Equal(safe ? url : null, dto.Url);
            Assert.Equal(document.ExternalId, dto.ExternalId);
            Assert.Equal(document.Title, dto.Title);
            var wire = JsonSerializer.SerializeToElement(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(safe ? url : null, wire.GetProperty("url").GetString());
            Assert.False(wire.TryGetProperty("id", out _)); // Mongo ID is never turned into a publisher URL.
        }
    }

    private sealed class Repository(NewsDocument document) : INewsRepository
    {
        public Task<NewsDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default) => Task.FromResult<NewsDocument?>(document);
        public Task<List<NewsDocument>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default) => Task.FromResult(new List<NewsDocument> { document });
        public Task<ObjectId> AddAsync(NewsDocument news, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<NewsDocument?> GetByExternalIdAndSourceAsync(string externalId, NewsSource source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReplaceAsync(NewsDocument news, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
