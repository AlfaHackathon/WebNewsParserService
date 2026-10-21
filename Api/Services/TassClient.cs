using AlfaHackaton.HttpClient.Common.Interfaces;
using Api.Models.Tass;
using Api.Services.Interfaces;
using Api.Settings;

namespace Api.Services;

/// <summary>
/// Клиент API tass.ru. BaseAddress — из настроек (Settings/TassClientSettings.cs).
/// </summary>
public class TassClient(IHttpClient<TassClientSettings> http, ILogger<TassClient> logger) : ITassClient
{
    /// <summary>Плоская лента «главной»: /tgap/api/v1/messages/search?lang=ru&limit=N</summary>
    public async Task<List<TassMainItem>?> GetLatestAsync(int limit, CancellationToken cancellationToken = default)
    {
        var response = await http.Get<TassMainResponse>($"tgap/api/v1/messages/search?lang=ru&limit={limit}", cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("TASS лента: статус {Status}, тело: {Body}", response.Status, response.TextData);
            return null;
        }

        return response.Result?.Result;
    }

    /// <summary>Поиск: /tbp/api/v1/search?search=...&limit=N&lang=ru</summary>
    public async Task<List<TassSearchItem>?> SearchAsync(string search, int limit, CancellationToken cancellationToken = default)
    {
        var path = $"tbp/api/v1/search?search={Uri.EscapeDataString(search)}&limit={limit}&lang=ru";
        var response = await http.Get<TassSearchResponse>(path, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("TASS поиск: статус {Status}, тело: {Body}", response.Status, response.TextData);
            return null;
        }

        return response.Result?.Result?.Contents;
    }
}
