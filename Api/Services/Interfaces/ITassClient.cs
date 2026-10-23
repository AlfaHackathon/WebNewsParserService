using Api.Models.Tass;

namespace Api.Services.Interfaces;

/// <summary>Клиент tass.ru (внешний API, ходим через AlfaHackaton.HttpClient).</summary>
public interface ITassClient
{
    /// <summary>Новости «главной» ленты (последние limit). null — TASS недоступен/ответ не 2xx.</summary>
    Task<List<TassMainItem>?> GetLatestAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Поиск по сайту TASS. null — недоступен/ответ не 2xx.</summary>
    Task<List<TassSearchItem>?> SearchAsync(string search, int limit, CancellationToken cancellationToken = default);

}
