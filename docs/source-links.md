# Stored publisher URL in internal news DTOs

`NewsSearchResponse` adds backwards-compatible nullable `url`, copied only from the saved `NewsDocument.Url` in get-by-id and stored search. Unsafe/missing values become null; accepted URLs are HTTP(S), no userinfo, whitespace/control/backslash, length <=4096. URLs are never constructed from ObjectId, publisher externalId, title or text. Existing Kafka events, collection contents and parsing workers are unchanged.

Agent's companion preserves optional DTO URL in Context admission as `sourceUrl`, which Context already persists in exact material provenance. Insight's companion exposes it through public `sources[].url`. Legacy saved materials remain null; no backfill or manual card edits are included.

Tests in `Tests/Api.Tests.csproj` are in-memory and run no MongoDB, Kafka, browser, poller or LLM: `dotnet test Tests/Api.Tests.csproj -c Release --filter FullyQualifiedName~NewsSourceLinkTests`. A separate linked harness compiled the actual NewsService/DTO/model/interface source with explicit Kafka/worker substitutes and public MongoDB.Bson: 12 tests pass. This is not native organizational package verification. The linked compile reports the pre-existing nullable Alfa feed dereference at NewsService.cs:70; this change does not edit that polling code. Native restore/build/test requires the authorized root executor's organizational feed access; Insight's restore is blocked by NU1301/proxy403 in cloud.

Source-only; root owns integration and deployment with the single remote writer. Companion branches: Agent `codex/parser-source-url-20261026`, Insight `codex/insight-source-links-20261026`.
