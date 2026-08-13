using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/albums")]
public class AlbumsController(CatalogService catalog, StreamingService streaming) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlbumDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? artistId,
        [FromQuery] string? q = null,
        [FromQuery] bool recentFirst = false,
        CancellationToken ct = default) =>
        Ok(await catalog.GetAlbumsAsync(new PageRequest(page, pageSize), artistId, recentFirst, q, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlbumDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetAlbumAsync(id, ct));

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenAlbumCoverAsync(id, size, ct));
}
