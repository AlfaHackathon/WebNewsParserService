using AlfaHackaton.HttpClient.Common.Settings;

namespace Api.Settings;

/// <summary>
/// Настройки клиента «РБК Инвестиций» (appsettings: секция Rbc), ходим через AlfaHackaton.HttpClient.
/// Антибота нет: сайт — Next.js, данные лежат в SSR-состоянии
/// (&lt;script id="__NEXT_DATA__"&gt;) страницы ленты и статьи.
/// </summary>
public class RbcClientSettings : HttpClientSettings
{
    /// <summary>URL ленты (materials + newsfeed лежат в NEXT_DATA страницы /quote).</summary>
    public string FeedPagePath { get; }

    /// <summary>
    /// Сколько статей за один цикл ходим открывать ради полного текста.
    /// У остальных сохраняется пустой текст — как у TASS-воркера.
    /// </summary>
    public int MaxArticleFetchPerPoll { get; }

    public RbcClientSettings(IConfiguration configuration)
    {
        BaseAddress = configuration["Rbc:BaseAddress"] ?? "https://www.rbc.ru";
        FeedPagePath = configuration["Rbc:FeedPagePath"] ?? "/quote";
        MaxArticleFetchPerPoll = configuration.GetValue<int?>("Rbc:MaxArticleFetchPerPoll") ?? 10;

        Timeout = TimeSpan.FromSeconds(30);

        // лента рендерится фронтендом Next.js — заголовки как у обычного браузера
        DefaultHeaders = headers =>
        {
            headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36");
            headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Google Chrome\";v=\"155\", \"Chromium\";v=\"155\", \"Not(A:Brand\";v=\"24\"");
            headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9");
        };
    }
}
