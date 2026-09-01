// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Api.Startup;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Байты трека: прогрессивный поток, HLS, скачивание и обложка.
/// </summary>
/// <remarks>
/// Отдельно от каталога, потому что здесь у каждого действия свои заголовки кэширования, и
/// именно они — содержание этих методов, а не вызов сервиса.
/// </remarks>
[ApiController]
[Route("api/tracks")]
public class TrackMediaController(StreamingService streaming) : ControllerBase
{
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

    [HttpGet("{id:guid}/hls/master.m3u8")]
    [Produces("application/vnd.apple.mpegurl")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> HlsMaster(
        Guid id, [FromQuery] AudioQuality maxQuality = AudioQuality.Normal, CancellationToken ct = default)
    {
        var manifest = await streaming.OpenHlsMasterAsync(id, maxQuality, ct);
        Response.Headers.ETag = manifest.ETag;

        if (!manifest.Ready)
        {
            // «Готовлю» — состояние на секунды, кэшировать его нельзя: раньше оно жило 30 секунд
            // вместе со своим ETag и держало клиента на прогрессивном фолбэке дольше, чем нужно.
            Response.Headers.CacheControl = "no-store";
            Response.Headers.RetryAfter = "2";
            return Accepted();
        }

        // Готовый мастер меняется только когда доезжает ещё одна вариация, и это отражено в ETag.
        Response.Headers.CacheControl = "private, max-age=3600, stale-while-revalidate=86400";

        return Content(manifest.Content!, "application/vnd.apple.mpegurl");
    }

    [HttpGet("{id:guid}/hls/{quality}/{fileName}")]
    [Produces("application/vnd.apple.mpegurl", "audio/mp4")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HlsAsset(
        Guid id, AudioQuality quality, string fileName, CancellationToken ct = default)
    {
        var asset = await streaming.OpenHlsAssetAsync(id, quality, fileName, ct);
        Response.Headers.ETag = asset.ETag;

        // Вариантный плейлист — это VOD: после того как ffmpeg его дописал, он не меняется никогда,
        // ровно как и сегменты. Прежние 30 секунд с must-revalidate стоили лишнего round-trip
        // на каждом старте трека.
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return File(asset.Content, asset.ContentType);
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

    [HttpGet("{id:guid}/cover")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenTrackCoverAsync(id, size, ct));
}
