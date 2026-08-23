// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/artists")]
public class ArtistsController(
    CatalogService catalog,
    ArtistProfileService profiles,
    StreamingService streaming,
    RecommendationService recommendations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ArtistDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q,
        CancellationToken ct) =>
        Ok(await catalog.GetArtistsAsync(new PageRequest(page, pageSize), q, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArtistDetailDto>> Get(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct) =>
        Ok(await catalog.GetArtistAsync(id, new PageRequest(page, pageSize), ct));

    // Обе секции живут отдельно от `GET {id}`: та перезапрашивается на каждой смене страницы
    // треков, и таскать с собой топ с похожими было бы чистой тратой.
    [HttpGet("{id:guid}/top-tracks")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> TopTracks(
        Guid id, [FromQuery] int limit = 10, CancellationToken ct = default) =>
        Ok(await catalog.GetArtistTopTracksAsync(id, Math.Clamp(limit, 1, 50), ct));

    [HttpGet("{id:guid}/similar")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ArtistDto>>> Similar(
        Guid id, [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetSimilarArtistsAsync(id, limit, ct));

    [HttpGet("{id:guid}/image")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct) =>
        this.ImageFile(await streaming.OpenArtistImageAsync(id, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArtistDto>> Update(Guid id, UpdateArtistRequest request, CancellationToken ct) =>
        Ok(await profiles.RenameAsync(id, request, ct));

    [HttpPost("{id:guid}/image")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ArtistDto>> UploadImage(Guid id, IFormFile? file, CancellationToken ct)
    {
        var image = file.RequireImage();

        await using var stream = image.OpenReadStream();
        return Ok(await profiles.SetImageAsync(id, stream, image.ContentType, image.FileName, image.Length, ct));
    }

    [HttpDelete("{id:guid}/image")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct)
    {
        await profiles.RemoveImageAsync(id, ct);
        return NoContent();
    }
}
