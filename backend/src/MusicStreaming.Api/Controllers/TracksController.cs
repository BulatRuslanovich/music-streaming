using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
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

    /// <summary>
    /// Streams the audio file. <c>enableRangeProcessing</c> is what makes seeking work: ASP.NET
    /// answers a <c>Range</c> header with <c>206 Partial Content</c> plus <c>Content-Range</c>,
    /// advertises <c>Accept-Ranges: bytes</c>, and copies only the requested window from the
    /// FileStream — the file is never read into memory as a whole.
    /// </summary>
    [HttpGet("{id:guid}/stream")]
    public async Task<IActionResult> Stream(Guid id, CancellationToken ct)
    {
        var audio = await streaming.OpenTrackAsync(id, ct);

        // The file contents never change, so it is safe for the browser to reuse its copy.
        Response.Headers.CacheControl = "private, max-age=604800";

        // FileStreamResult disposes the stream once the response has been written.
        return File(
            audio.Content,
            audio.ContentType,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    /// <summary>Cover art for the track, resolved through its album.</summary>
    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(Guid id, CancellationToken ct)
    {
        var cover = await streaming.OpenTrackCoverAsync(id, ct);
        Response.Headers.CacheControl = "private, max-age=604800";

        return File(cover.Content, cover.ContentType);
    }

    /// <summary>
    /// Accepts one or many MP3 files. Each file is validated, stored and tagged independently, so
    /// the response reports per-file failures instead of rejecting the whole batch.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(long.MaxValue)] // the real ceiling comes from Kestrel and StorageOptions
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

        // A batch where nothing succeeded is a failed request, not a successful empty one.
        if (result.Uploaded.Count == 0)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TrackDto>> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) =>
        Ok(await library.UpdateTrackAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
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
