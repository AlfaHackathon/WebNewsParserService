using Api.Common;
using Api.Models;
using MongoDB.Bson;

namespace Api.Repositories.Interfaces;

public interface INewsRepository
{
    /// <summary>Вставить новость. false — уже есть (дубликат Source+ExternalId), сохранять нечего.</summary>
    Task<ObjectId> AddAsync(NewsDocument news, CancellationToken cancellationToken = default);
    Task<NewsDocument?> GetByExternalIdAndSourceAsync(string externalId, NewsSource source, CancellationToken cancellationToken = default);
    /// <summary>Поиск по заголовку и тексту (case-insensitive). Свежие первыми.</summary>
    Task<List<NewsDocument>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default);
    Task<bool> ReplaceAsync(NewsDocument news, CancellationToken cancellationToken = default);
    public Task<NewsDocument?> GetByIdAsync(ObjectId id, CancellationToken cancellationToken = default);
}
