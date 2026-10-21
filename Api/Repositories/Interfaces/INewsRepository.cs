using Api.Models;

namespace Api.Repositories.Interfaces;

public interface INewsRepository
{
    /// <summary>Вставить новость. false — уже есть (дубликат Source+ExternalId), сохранять нечего.</summary>
    Task<bool> TryAddAsync(NewsDocument news, CancellationToken cancellationToken = default);

    /// <summary>Поиск по заголовку и тексту (case-insensitive). Свежие первыми.</summary>
    Task<List<NewsDocument>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default);
}
