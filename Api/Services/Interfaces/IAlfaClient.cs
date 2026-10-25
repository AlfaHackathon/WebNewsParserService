using Api.Models.Alfa;

namespace Api.Services.Interfaces;

/// <summary>
/// Клиент «Альфа-Инвестора». Сайт за антиботом (ServicePipe) и за сертификатом
/// НУЦ Минцифры — поэтому весь ход через настоящий браузер (Playwright).
/// </summary>
public interface IAlfaClient
{
    /// <summary>
    /// Страница ленты через GraphQL-эндпоинт (POST /api/v1/eye-of-investor/graphql).
    /// null — эндпоинт недоступен / антибот не пропустил.
    /// </summary>
    Task<AlfaFeed?> GetFeedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Полная статья по слагу (path из ленты). null — не удалось вытащить.
    /// </summary>
    Task<AlfaArticle?> GetArticleAsync(string path, CancellationToken cancellationToken = default);
}
