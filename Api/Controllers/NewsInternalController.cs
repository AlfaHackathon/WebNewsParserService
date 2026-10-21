using Api.Contracts;
using Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// internal-ручки парсера: обе search (по Mongo и по TASS) для других сервисов кластера.
/// Защищены заголовком X-Internal-Secret. Префикс news-parser-service глобальный.
/// </summary>
[ApiController]
public class NewsInternalController(INewsService news) : ControllerBase
{
    private const string InternalSecretHeader = "X-Internal-Secret";

    /// <summary>Поиск по сохранённым новостям (Монга): заголовок и текст, case-insensitive.</summary>
    [HttpGet("internal/news/search")]
    public async Task<IActionResult> Search([FromQuery] string search, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!IsInternalRequest()) return Unauthorized(new { error = "Только для внутренних вызовов" });
        if (string.IsNullOrWhiteSpace(search)) return BadRequest(new { error = "Пустой поисковый запрос" });

        var found = await news.SearchAsync(search.Trim(), Math.Clamp(limit, 1, 100), ct);
        return Ok(found);
    }

    /// <summary>Поиск по TASS (proxy, без сохранения).</summary>
    [HttpGet("internal/news/tass-search")]
    public async Task<IActionResult> TassSearch([FromQuery] string search, [FromQuery] int limit = 6, CancellationToken ct = default)
    {
        if (!IsInternalRequest()) return Unauthorized(new { error = "Только для внутренних вызовов" });
        if (string.IsNullOrWhiteSpace(search)) return BadRequest(new { error = "Пустой поисковый запрос" });

        var items = await news.SearchTassAsync(search.Trim(), Math.Clamp(limit, 1, 20), ct);
        if (items is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "TASS недоступен" });
        }

        return Ok(items.Select(i => new TassSearchResponse(
            i.CompositeId, i.Title, i.Lead, TassFullUrl(i.Url), i.PublishedDt)).ToList());
    }

    /// <summary>Health-check.</summary>
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { status = "ok" });

    private static string TassFullUrl(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://tass.ru{url}";

    private bool IsInternalRequest()
    {
        var expected = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Internal:Secret"];
        var provided = Request.Headers[InternalSecretHeader].ToString();
        return !string.IsNullOrEmpty(expected) && expected == provided;
    }
}
