# WebNewsParserService

Парсер новостей с внешних источников: воркеры (BackgroundService) опрашивают
источники, сохраняют новое в Mongo и шлют уведомления в Kafka.
Стек: .NET 10, MongoDB (`AlfaHackaton.MongoContext`), Kafka (`AlfaHackaton.Kafka`),
HTTP-клиенты (`AlfaHackaton.HttpClient`).

## Источники

| Воркер | Источник | API | Статус |
|---|---|---|---|
| `TassNewsWorker` | **Тасс** | `https://tass.ru/tgap/api/v1/messages/search?lang=ru&limit=50` (лента главной), `https://tass.ru/tbp/api/v1/search?search=...` (поиск) | сделан |
| `AlfaNewsWorker` | **Альфа-Инвестор** | GraphQL `POST https://alfabank.ru/api/v1/eye-of-investor/graphql` (лента), SSR-состояние страниц статей (`script#app_state`) | сделан (ветка `alfa-news`) |
| RBC | rbc.ru | — | TODO |

## Как работает воркер TASS

1. Раз в `Worker:TassPollIntervalSeconds` (дефолт 30с) забирает ленту главной (50 шт).
2. Берёт только `status == "published"`.
3. Вставку в Mongo — дедупликация по уникальному индексу `(Source, ExternalId)`.
4. Новую новость шлёт в Kafka: топик `news-parser.news`, сообщение `NewsEventMessage
   { Source, ExternalId, Title, Text, PublishedAt }`.
5. Недоступный TASS/Kafka/Mongo не валит цикл — ретрай в следующий тик.

Заголовки: tass.ru режет запросы без User-Agent (403), поэтому в
`TassClientSettings` проставлен браузерный UA.

## Как работает воркер Альфа

У alfabank.ru нет публичного новостного API, и обычный HttpClient с наскоку тоже
не работает: сайт за антибот-челленджем ServicePipe и за сертификатом НУЦ Минцифры.
Поэтому клиент (`Services/AlfaClient.cs`) сделан **гибридным**:

1. **Основной транспорт — HttpClient.** Антибот-челлендж пропускается по кукам
   антибот-сессии (`spid/spsn...`, живёт ограниченное время): один раз вручную
   достаём из браузера (DevTools → Network → любой запрос → скопировать значение
   `Cookie`) и кладём в `Alfa:ManualCookies`. Дальше `CookieContainer` сам
   поддерживает сессию из ответов сайта. TLS-проверку отключаем в
   `HttpClientHandler` (сертификат Минцифры в docker-контейнер вставлять некуда).
2. **Страховка — Playwright.** Если куки протухли и транспорт получил заглушку
   (маркер `js-challenge-loader` в теле / 403 / 401), раз в минуту максимум
   поднимается headless Chromium, проходит челлендж и свежие куки подмешиваются
   в общий контейнер — HttpClient снова работает сам.

Данные забираем тем же способом, что их собственный фронтенд:

1. **Лента** — их внутренний GraphQL (анонимный, работает и с курсором):
   `POST https://alfabank.ru/api/v1/eye-of-investor/graphql`, запрос `GetFeedPublications`,
   переменные: `{ page: { limit, cursor? }, rubricCodes: { in: ["news"] }, filterType: { eq: "rubrics" }, isAuthorized: false }`.
   В ответе — `uuid`, `publishAt`, заголовок, автор, привязанные тикеры (ISIN/ticker),
   `pagination.nextCursor`.
2. **Полный текст** — в ленте тела статьи нет: открываем страницу статьи
   `/alfa-investor/t/<path>/` и вытаскиваем вшитое SSR-состояние
   (`script#app_state` → `pageContent.fetchResults[].response.value.article`),
   там `body` — полный текст уже в Markdown.

Dедупликация — по `uuid` статьи (уникальный индекс `(Source, ExternalId)`). За один
цикл проходим до `Alfa:MaxPagesPerPoll` страниц курсором (страница без нового =
дальше всё старое) и открываем не более `Alfa:MaxArticleFetchPerPoll` статей ради
полного текста (открытие страницы — секунды). У остальных сохраняется пустой
текст — как у TASS-воркера.

Настройки — `internal/news` + конфиг `Alfa__*` (см. Конфигурацию).

Локально перед первым запуском один раз поставить браузер для Playwright:

```bash
pwsh Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

## Хранилище

Mongo, база **`news-parser`** (одна база на сервис — правило agent.md), коллекция `news`:

```
NewsDocument { _id (ObjectId), Source, ExternalId, Title, Text, PublishedDt, StoredAt }
```

Индексы (создаются при старте, идемпотентно): уникальный `(Source, ExternalId)`,
текстовый `(Title, Text)`.

## Ручки (обе internal, заголовок `X-Internal-Secret`)

| Метод | Путь | Query | Что делает |
|---|---|---|---|
| GET | `/news-parser-service/internal/news/search` | `search`, `limit=50` | поиск по Монге (заголовок + текст, case-insensitive), свежие первыми |
| GET | `/news-parser-service/internal/news/tass-search` | `search`, `limit=6` | proxy-поиск по tass.ru (без сохранения), 503 если TASS недоступен |
| GET | `/news-parser-service/ping` | — | health-check |

## Kafka-сообщение

```
{"Source":"tass","ExternalId":"28236629","Title":"...","Text":"","PublishedAt":"2026-10-21T17:57:54Z"}
```

Топик `news-parser.news` (env `Kafka__Topic`). Если брокер временно недоступен,
produce не блокирует парсинг дольше 10с; Confluent.Kafka доставит когда поднимется.

## Конфигурация

- `Mongo__ConnectionString` / `Mongo__Database` (дефолт `news-parser`)
- `Tass__BaseAddress` (дефолт `https://tass.ru`)
- `Alfa__BaseAddress` (дефолт `https://alfabank.ru`), `Alfa__Rubric` (дефолт `news`),
  `Alfa__Limit` (20), `Alfa__MaxPagesPerPoll` (5), `Alfa__MaxArticleFetchPerPoll` (10),
  `Alfa__ChallengeWaitTimeoutSeconds` (90), `Alfa__ManualCookies` (пусто — тогда куки
  добываются автоматически headless-браузером)
- `Kafka__BootstrapServers`, `Kafka__Topic`, `Kafka__GroupId`
- `Worker__TassPollIntervalSeconds`, `Worker__AlfaPollIntervalSeconds` (300)
- `Internal__Secret` — общий секрет кластера (internal-ручки)
- `Swagger__Enabled`

## Запуск локально

Нужны Mongo (:-27017/27018 в dev-кластере) и Kafka:

```bash
$env:Mongo__ConnectionString='mongodb://localhost:27018'
$env:Internal__Secret='<Internal__Secret из .env>'
dotnet run --urls http://127.0.0.1:8086
```

Либо весь кластер: `cd ../cluster; docker compose -f docker-compose-dev.yaml up -d --build`
(порты сервиса: 8086, kafka наружу: 9094).
