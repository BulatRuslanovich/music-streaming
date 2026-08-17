using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Главная страница в её общем, не персональном виде: что вообще есть в библиотеке.
///
/// <para>
/// Отличается от <c>api/recommendations/home</c> тем, что ничего не знает про вкус: свежие
/// поступления, случайные подборки, счётчики. Именно это видит человек, у которого ещё нет истории
/// прослушиваний, и именно это остаётся, если движок рекомендаций выключен.
/// </para>
/// </summary>
[ApiController]
[Route("api/home")]
public class HomeController(CatalogService catalog) : ControllerBase
{
    /// <summary>Сводка для главной страницы.</summary>
    /// <param name="sectionSize">Сколько объектов в каждой секции; зажимается в диапазон 1..50, поэтому запрос заведомо большого числа не ошибка, а просто максимум.</param>
    /// <param name="ct">Токен отмены.</param>
    [HttpGet]
    public async Task<ActionResult<HomeSummaryDto>> Get([FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await catalog.GetHomeSummaryAsync(Math.Clamp(sectionSize, 1, 50), ct));
}
