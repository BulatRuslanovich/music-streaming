using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Альбомы.
///
/// <para>
/// Как и исполнители, заводятся при загрузке треков из тегов, а не отдельной ручкой. Альбом
/// опознаётся парой «исполнитель и нормализованное название», поэтому одноимённые сборники разных
/// исполнителей не сливаются в один.
/// </para>
/// </summary>
[ApiController]
[Route("api/albums")]
public class AlbumsController(CatalogService catalog, StreamingService streaming) : ControllerBase
{
    /// <summary>Список альбомов.</summary>
    /// <param name="artistId">Оставить только альбомы этого исполнителя.</param>
    /// <param name="q">Подстрока названия.</param>
    /// <param name="recentFirst">Сортировать по времени добавления в библиотеку, а не по названию.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlbumDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? artistId,
        [FromQuery] string? q = null,
        [FromQuery] bool recentFirst = false,
        CancellationToken ct = default) =>
        Ok(await catalog.GetAlbumsAsync(new PageRequest(page, pageSize), artistId, recentFirst, q, ct));

    /// <summary>Альбом вместе со списком его треков в порядке номеров.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlbumDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetAlbumAsync(id, ct));

    /// <summary>
    /// Обложка альбома, webp.
    ///
    /// <para>
    /// Берётся из тегов первого загруженного трека, у которого она была. Если запрошенного размера
    /// на диске нет — например, обложка сохранена до того, как размеров стало два, — отдаётся полная
    /// версия, а не 404.
    /// </para>
    /// </summary>
    /// <param name="size">Размер: полный (640 px) или миниатюра (256 px) для списков.</param>
    [HttpGet("{id:guid}/cover")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenAlbumCoverAsync(id, size, ct));
}
