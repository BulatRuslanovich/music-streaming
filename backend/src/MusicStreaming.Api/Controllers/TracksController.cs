// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Треки: чтение каталога, воспроизведение, загрузка и правка.
/// </summary>
[ApiController]
[Route("api/tracks")]
public class TracksController(
    CatalogService catalog,
    TrackEditService editor,
    TrackUploadService upload,
    UploadProbeService uploadProbe,
    StreamingService streaming,
    FavoriteService favorites,
    LyricsService lyrics,
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

    [HttpGet("shuffle")]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> Shuffle(
        [FromQuery] int? limit = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default) =>
        Ok(await catalog.GetShuffledTracksAsync(limit, q, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrackDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetTrackAsync(id, ct));

    [HttpGet("{id:guid}/stream")]
    [Produces("audio/mpeg", "audio/flac", "audio/mp4", "audio/ogg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stream(
        Guid id, [FromQuery] AudioQuality? quality = null, CancellationToken ct = default)
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
    [Produces("audio/mpeg", "audio/flac", "audio/mp4")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
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

    [HttpGet("{id:guid}/similar")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RecommendedTrackDto>>> Similar(
        Guid id, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await recommendations.GetSimilarAsync(id, limit, includeScores: false, ct));

    [HttpGet("{id:guid}/lyrics")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LyricsDto>> Lyrics(Guid id, CancellationToken ct) =>
        await lyrics.GetAsync(id, ct) is { } found ? Ok(found) : NoContent();

    [HttpPut("{id:guid}/lyrics")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LyricsDto>> UpdateLyrics(
        Guid id, UpdateLyricsRequest request, CancellationToken ct) =>
        await lyrics.ReplaceAsync(id, request.Text, ct) is { } saved ? Ok(saved) : NoContent();

    [HttpGet("{id:guid}/cover")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenTrackCoverAsync(id, size, ct));

    [HttpPost("upload/check")]
    public async Task<ActionResult<UploadProbeResultDto>> CheckUpload(
        UploadProbeRequest request, CancellationToken ct) =>
        Ok(await uploadProbe.ProbeAsync(request.Files ?? [], ct));

    [HttpPost("upload")]
    [ProducesResponseType<UploadResultDto>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<UploadResultDto>> Upload(CancellationToken ct)
    {
        if (Request.Headers["X-File-Name"].FirstOrDefault() is not { Length: > 0 } encodedName)
            throw new ValidationException("The X-File-Name header is required.");

        string fileName;
        try
        {
            fileName = Uri.UnescapeDataString(encodedName);
        }
        catch (UriFormatException)
        {
            throw new ValidationException("The X-File-Name header is not valid.");
        }

        var candidate = new UploadCandidate(
            fileName,
            Request.ContentType,
            Request.ContentLength ?? -1,
            () => Request.Body);

        var result = await upload.UploadAsync(candidate, ct);

        return result.Uploaded.Count == 0 ? BadRequest(result) : Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TrackDto>> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) =>
        Ok(await editor.UpdateTrackAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await editor.DeleteTrackAsync(id, ct);
        return NoContent();
    }

    [HttpPost("bulk-delete")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkDeleteResultDto>> BulkDelete(
        BulkDeleteTracksRequest request, CancellationToken ct) =>
        Ok(await editor.DeleteTracksAsync(request.Ids ?? [], ct));

    [HttpPost("{id:guid}/favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddFavorite(Guid id, CancellationToken ct)
    {
        await favorites.AddAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveFavorite(Guid id, CancellationToken ct)
    {
        await favorites.RemoveAsync(id, ct);
        return NoContent();
    }
}
