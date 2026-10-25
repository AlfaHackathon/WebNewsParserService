namespace Api.Settings;

/// <summary>
/// Настройки клиента «РБК Инвестиций» (appsettings: секция Rbc).
/// Антибота нет: сайт — Next.js, данные лежат в SSR-состоянии
/// (&lt;script id="__NEXT_DATA__"&gt;) страницы ленты и статьи.
/// </summary>
public class RbcClientSettings
{
    public string BaseAddress { get; }

    /// <summary>URL ленты (вкладка «Лента» на quote — данные materials+newsfeed в NEXT_DATA).</summary>
    public string FeedPagePath { get; }

    /// <summary>Сколько статей за один цикл ходим открывать ради полного текста.</summary>
    public int MaxArticleFetchPerPoll { get; }

    public RbcClientSettings(IConfiguration configuration)
    {
        BaseAddress = configuration["Rbc:BaseAddress"] ?? "https://www.rbc.ru";
        FeedPagePath = configuration["Rbc:FeedPagePath"] ?? "/quote";
        MaxArticleFetchPerPoll = configuration.GetValue<int?>("Rbc:MaxArticleFetchPerPoll") ?? 10;
    }
}
