# WebNewsParserService

Парсер новостей с внешних источников: воркеры (BackgroundService) опрашивают
источники, сохраняют новое в Mongo и шлют уведомления в Kafka.
Стек: .NET 10, MongoDB (`AlfaHackaton.MongoContext`), Kafka (`AlfaHackaton.Kafka`),
HTTP-клиенты (`AlfaHackaton.HttpClient`).

## Источники

| Воркер | Источник | API | Статус |
|---|---|---|---|
| `TassNewsWorker` | **Тасс** | `https://tass.ru/tgap/api/v1/messages/search?lang=ru&limit=50` (лента главной), `https://tass.ru/tbp/api/v1/search?search=...` (поиск) | сделан |
| RBC | rbc.ru | — | TODO |
| Alfa | — | — | TODO |

## Как работает воркер TASS

1. Раз в `Worker:TassPollIntervalSeconds` (дефолт 30с) забирает ленту главной (50 шт).
2. Берёт только `status == "published"`.
3. Вставку в Mongo — дедупликация по уникальному индексу `(Source, ExternalId)`.
4. Новую новость шлёт в Kafka: топик `news-parser.news`, сообщение `NewsEventMessage
   { Source, ExternalId, Title, Text, PublishedAt }`.
5. Недоступный TASS/Kafka/Mongo не валит цикл — ретрай в следующий тик.

Заголовки: tass.ru режет запросы без User-Agent (403), поэтому в
`TassClientSettings` проставлен браузерный UA.

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
- `Kafka__BootstrapServers`, `Kafka__Topic`, `Kafka__GroupId`
- `Worker__TassPollIntervalSeconds`
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
