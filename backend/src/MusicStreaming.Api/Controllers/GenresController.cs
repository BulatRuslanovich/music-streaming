using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/genres")]
public class GenresController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> List(CancellationToken ct) =>
        Ok(await catalog.GetGenresAsync(ct));

    [HttpGet("{id:guid}/tracks")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await catalog.GetGenreTracksAsync(id, new PageRequest(page, pageSize), ct));
}