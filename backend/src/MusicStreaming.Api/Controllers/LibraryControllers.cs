using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/artists")]
public sealed class ArtistsController(LibraryService library) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ArtistDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await library.GetArtistsAsync(new PageRequest(page, pageSize), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await library.GetArtistAsync(id, ct));
}

[ApiController]
[Route("api/albums")]
public sealed class AlbumsController(LibraryService library, StreamingService streaming) : ControllerBase
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
        Response.Headers.CacheControl = "private, max-age=604800";

        return File(cover.Content, cover.ContentType);
    }
}

[ApiController]
[Route("api/genres")]
public sealed class GenresController(LibraryService library) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> List(CancellationToken ct) =>
        Ok(await library.GetGenresAsync(ct));

    [HttpGet("{id:guid}/tracks")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await library.GetGenreTracksAsync(id, new PageRequest(page, pageSize), ct));
}

[ApiController]
[Route("api/search")]
public sealed class SearchController(SearchService search) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await search.SearchAsync(q, limit, ct));
}

/// <summary>Aggregated payload for the home page, so the first screen is one request.</summary>
[ApiController]
[Route("api/home")]
public sealed class HomeController(LibraryService library) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HomeSummaryDto>> Get([FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await library.GetHomeSummaryAsync(Math.Clamp(sectionSize, 1, 50), ct));
}
