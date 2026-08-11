using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/tracks")]
public sealed class TracksController(
    LibraryService library,
    TrackUploadService upload,
    StreamingService streaming,
    FavoriteService favorites) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] LibraryService.TrackSort sort = LibraryService.TrackSort.Title,
        CancellationToken ct = default) =>
        Ok(await library.GetTracksAsync(new PageRequest(page, pageSize), sort, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrackDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await library.GetTrackAsync(id, ct));

    [HttpGet("{id:guid}/stream")]
    public async Task<IActionResult> Stream(Guid id, CancellationToken ct)
    {
        var audio = await streaming.OpenTrackAsync(id, ct);

        Response.Headers.CacheControl = "private, max-age=604800";

        return File(
            audio.Content,
            audio.ContentType,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(Guid id, CancellationToken ct)
    {
        var cover = await streaming.OpenTrackCoverAsync(id, ct);
        Response.Headers.CacheControl = "private, max-age=604800";

        return File(cover.Content, cover.ContentType);
    }

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
        Ok(await library.UpdateTrackAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await library.DeleteTrackAsync(id, ct);
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
