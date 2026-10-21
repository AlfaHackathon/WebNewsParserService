namespace Api.Settings;

/// <summary>
/// Настройки клиента «Альфа-Инвестора» (appsettings: секция Alfa).
/// Обычный HttpClient не подходит: сайт за антиботом (ServicePipe) — запросы
/// ходят через Playwright (настоящий Chromium), поэтому настроек немного.
/// </summary>
public class AlfaClientSettings
{
    public string BaseAddress { get; }

    /// <summary>Рубрика ленты: news / analytics / bonds / dividends / economy_and_trands ...</summary>
    public string Rubric { get; }

    /// <summary>Сколько публикаций забирать за один запрос GraphQL.</summary>
    public int Limit { get; }

    /// <summary>Сколько страниц ленты проходить за один цикл опроса (до первой уже известной пабликиации).</summary>
    public int MaxPagesPerPoll { get; }

    /// <summary>
    /// Сколько статей за один цикл ходим открывать ради полного текста (открытие
    /// страницы — несколько секунд, чтобы не засиживаться).
    /// У остальных сохраняется пустой текст — как у TASS-воркера.
    /// </summary>
    public int MaxArticleFetchPerPoll { get; }

    /// <summary>Сколько секунд максимум ждать прохода антибот-челленджа.</summary>
    public int ChallengeWaitTimeoutSeconds { get; }

    public AlfaClientSettings(IConfiguration configuration)
    {
        BaseAddress = configuration["Alfa:BaseAddress"] ?? "https://alfabank.ru";
        Rubric = configuration["Alfa:Rubric"] ?? "news";
        Limit = configuration.GetValue<int?>("Alfa:Limit") ?? 20;
        MaxPagesPerPoll = configuration.GetValue<int?>("Alfa:MaxPagesPerPoll") ?? 5;
        MaxArticleFetchPerPoll = configuration.GetValue<int?>("Alfa:MaxArticleFetchPerPoll") ?? 10;
        ChallengeWaitTimeoutSeconds = configuration.GetValue<int?>("Alfa:ChallengeWaitTimeoutSeconds") ?? 90;
    }
}
