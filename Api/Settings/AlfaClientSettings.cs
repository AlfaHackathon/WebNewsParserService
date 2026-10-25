using AlfaHackaton.HttpClient.Common.Settings;

namespace Api.Settings;

/// <summary>
/// Настройки клиента «Альфа-Инвестора» (appsettings: секция Alfa).
/// Ходим через AlfaHackaton.HttpClient, поэтому транспортные параметры — запуск.
/// </summary>
public class AlfaClientSettings : HttpClientSettings
{
    /// <summary>Рубрика ленты: news / analytics / bonds / dividends / economy_and_trands ...</summary>
    public string Rubric { get; }

    /// <summary>Сколько публикаций забирать за один запрос GraphQL.</summary>
    public int Limit { get; }

    /// <summary>Сколько страниц ленты проходить за один цикл опроса (до первой уже известной пабликиации).</summary>
    public int MaxPagesPerPoll { get; }

    /// <summary>
    /// Сколько статей за один цикл ходим открывать ради полного текста.
    /// У остальных сохраняется пустой текст — как у TASS-воркера.
    /// </summary>
    public int MaxArticleFetchPerPoll { get; }

    public AlfaClientSettings(IConfiguration configuration)
    {
        BaseAddress = configuration["Alfa:BaseAddress"] ?? "https://alfabank.ru";
        Rubric = configuration["Alfa:Rubric"] ?? "news";
        Limit = configuration.GetValue<int?>("Alfa:Limit") ?? 20;
        MaxPagesPerPoll = configuration.GetValue<int?>("Alfa:MaxPagesPerPoll") ?? 5;
        MaxArticleFetchPerPoll = configuration.GetValue<int?>("Alfa:MaxArticleFetchPerPoll") ?? 10;

        // серт НУЦ Минцифры не в доверенных — отключаем проверку
        IgnoreVerifySsl = true;
        Timeout = TimeSpan.FromSeconds(30);

        // заголовки как у их же фронтенда (снято из запросов сайта) — часть пропуска WAF:
        // [куки без заголовков] -> 403, [куки + браузерные заголовки] -> 200
        DefaultHeaders = headers =>
        {
            headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36");
            headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Google Chrome\";v=\"155\", \"Chromium\";v=\"155\", \"Not(A:Brand\";v=\"24\"");
            headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9");
            headers.TryAddWithoutValidation("Origin", BaseAddress.TrimEnd('/'));
            headers.TryAddWithoutValidation("Referer", BaseAddress.TrimEnd('/') + "/alfa-investor/category/t/news/all-posts/");
            headers.Accept.ParseAdd("application/json");
        };
    }
}
