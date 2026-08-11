using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/albums")]
public class AlbumsController(LibraryService library, StreamingService streaming) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlbumDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? artistId,
        [FromQuery] bool recentFirst = false,
        CancellationToken ct = default) =>
        Ok(await library.GetAlbumsAsync(new PageRequest(page, pageSize), artistId, recentFirst, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlbumDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await library.GetAlbumAsync(id, ct));

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(Guid id, CancellationToken ct)
    {
        var cover = await streaming.OpenAlbumCoverAsync(id, ct);
        Response.Headers.CacheControl = "private, max-age=86400";

        return File(
            cover.Content,
            cover.ContentType,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(cover.ETag));
    }
}