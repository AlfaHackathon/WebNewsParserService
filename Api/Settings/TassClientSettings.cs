using AlfaHackaton.HttpClient.Common.Settings;

namespace Api.Settings;

/// <summary>
/// Настройки клиента tass.ru: BaseAddress — из appsettings (Tass:BaseAddress).
/// Секретов нет — внешний API, internal-заголовки не нужны.
/// </summary>
public class TassClientSettings : HttpClientSettings
{
    public TassClientSettings(IConfiguration configuration)
    {
        BaseAddress = configuration["Tass:BaseAddress"] ?? "https://tass.ru";
        Timeout = TimeSpan.FromSeconds(15);

        DefaultHeaders = headers =>
        {
            headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            headers.Accept.ParseAdd("application/json");
        };
    }
}

