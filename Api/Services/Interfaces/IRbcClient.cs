using Api.Models.Rbc;

namespace Api.Services.Interfaces;

/// <summary>
/// Клиент «РБК Инвестиций» (www.rbc.ru/quote). Антибота нет, но публичного API
/// тоже нет: сайт — Next.js, список ленты лежит в SSR-состоянии страницы
/// (&lt;script id="__NEXT_DATA__"&gt; → props.pageProps.materials / newsfeed).
/// </summary>
public interface IRbcClient
{
    /// <summary>
    /// Список публикаций со страницы ленты (materials + newsfeed из NEXT_DATA).
    /// null — сайт недоступен / состояние не найдено.
    /// </summary>
    Task<RbcItemsBlock?> GetFeedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Полная статья по URL публикации (props.pageProps.material.bodyMd в NEXT_DATA статьи).
    /// null — не удалось вытащить.
    /// </summary>
    Task<RbcArticle?> GetArticleAsync(string url, CancellationToken cancellationToken = default);
}
