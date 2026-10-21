using Api.Contracts;
using Api.Contracts.Kafka;
using Api.Models.Tass;

namespace Api.Services.Interfaces;

/// <summary>Бизнес-операции над новостями. Контроллеры и воркер работают только с сервисом.</summary>
public interface INewsService
{
    /// <summary>Один цикл парсинга TASS: забрал ленту, сохранил новое, о новом доложил в Kafka.</summary>
    Task PollTassAsync(CancellationToken cancellationToken = default);

    /// <summary>Один цикл парсинга «Альфа-Инвестора» (GraphQL + страницы статей).</summary>
    Task PollAlfaAsync(CancellationToken cancellationToken = default);

    /// <summary>Поиск по сохранённым новостям (Монга).</summary>
    Task<List<NewsSearchResponse>> SearchAsync(string search, int limit, CancellationToken cancellationToken = default);

    /// <summary>Proxy-поиск по TASS (без сохранения). null — TASS недоступен.</summary>
    Task<List<TassSearchItem>?> SearchTassAsync(string search, int limit, CancellationToken cancellationToken = default);
}
