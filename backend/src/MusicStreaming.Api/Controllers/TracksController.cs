using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/tracks")]
public class TracksController(
    CatalogService catalog,
    TrackEditService editor,
    TrackUploadService upload,
    StreamingService streaming,
    FavoriteService favorites,
    RecommendationService recommendations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q = null,
        [FromQuery] CatalogService.TrackSort sort = CatalogService.TrackSort.Title,
        CancellationToken ct = default) =>
        Ok(await catalog.GetTracksAsync(new PageRequest(page, pageSize), sort, q, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrackDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetTrackAsync(id, ct));

    [HttpGet("{id:guid}/stream")]
    public async Task<IActionResult> Stream(
        Guid id, [FromQuery] AudioQuality quality = AudioQuality.Original, CancellationToken ct = default)
    {
        var audio = await streaming.OpenTrackAsync(id, quality, ct);

        Response.Headers.CacheControl = "private, max-age=604800";

        return File(
            audio.Content,
            audio.ContentType,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var audio = await streaming.OpenTrackAsync(id, AudioQuality.Original, ct);

        Response.Headers.CacheControl = "private, no-store";

        return File(
            audio.Content,
            audio.ContentType,
            audio.DownloadName,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Tracks similar to this one. Mirrors <c>/api/recommendations/similar/{trackId}</c>; both
    /// spellings exist because sub-resources of a track live here by convention, while everything
    /// personalised is grouped under the recommendations route.
    /// </summary>
    [HttpGet("{id:guid}/similar")]
    public async Task<ActionResult<IReadOnlyList<RecommendedTrackDto>>> Similar(
        Guid id, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await recommendations.GetSimilarAsync(id, limit, includeScores: false, ct));

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenTrackCoverAsync(id, size, ct));

    [HttpPost("upload")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<ActionResult<UploadResultDto>> Upload(
        [FromForm(Name = "files")] IFormFileCollection? files,
        CancellationToken ct)
    {
        var incoming = files is { Count: > 0 } ? files : Request.Form.Files;
        if (incoming.Count == 0)
            throw new ValidationException("No files were provided.");

        var candidates = incoming
            .Select(f => new UploadCandidate(f.FileName, f.ContentType, f.Length, f.OpenReadStream))
            .ToList();

        var result = await upload.UploadAsync(candidates, ct);

        if (result.Uploaded.Count == 0)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<ActionResult<TrackDto>> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) =>
        Ok(await editor.UpdateTrackAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await editor.DeleteTrackAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/favorite")]
    public async Task<IActionResult> AddFavorite(Guid id, CancellationToken ct)
    {
        await favorites.AddAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/favorite")]
    public async Task<IActionResult> RemoveFavorite(Guid id, CancellationToken ct)
    {
        await favorites.RemoveAsync(id, ct);
        return NoContent();
    }
}
