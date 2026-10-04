// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using App.Common;
using App.Services;
using Domain.Common;

namespace Api.Controllers;

[ApiController]
[Route("api/tracks/{id:guid}")]
public class TrackMediaController(StreamingService streaming, CoverStreamService covers) : ControllerBase
{
    [HttpGet("stream")]
    [Produces("audio/mpeg", "audio/flac")]
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

    [HttpGet("hls/master.m3u8")]
    [Produces("application/vnd.apple.mpegurl")]
    public async Task<IActionResult> HlsMaster(
        Guid id, [FromQuery] AudioQuality maxQuality = AudioQuality.Normal, CancellationToken ct = default)
    {
        var manifest = await streaming.OpenHlsMasterAsync(id, maxQuality, ct);
        Response.Headers.ETag = manifest.ETag;

        if (!manifest.Ready)
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.RetryAfter = "2";
            return Accepted();
        }

        Response.Headers.CacheControl = "private, max-age=3600, stale-while-revalidate=86400";

        return Content(manifest.Content!, "application/vnd.apple.mpegurl");
    }

    [HttpGet("hls/{quality}/{fileName}")]
    [Produces("application/vnd.apple.mpegurl", "audio/mp4")]
    public async Task<IActionResult> HlsAsset(
        Guid id, AudioQuality quality, string fileName, CancellationToken ct = default)
    {
        var asset = await streaming.OpenHlsAssetAsync(id, quality, fileName, ct);

        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return File(
            asset.Content,
            asset.ContentType,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(asset.ETag),
            enableRangeProcessing: true);
    }

    [HttpGet("download")]
    [Produces("audio/mpeg", "audio/flac")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var audio = await streaming.OpenTrackAsync(id, ct);

        Response.Headers.CacheControl = "private, no-store";

        return File(
            audio.Content,
            audio.ContentType,
            audio.DownloadName,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    [HttpGet("cover")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await covers.OpenTrackCoverAsync(id, size, ct));
}
