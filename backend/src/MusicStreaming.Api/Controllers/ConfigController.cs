// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController(
    IOptions<PlaybackOptions> playback,
    IOptions<StorageOptions> storage,
    IOptions<TranscodeOptions> transcode,
    IOptions<JwtOptions> jwt,
    IAudioTranscoder transcoder) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(new ClientConfigDto(
        playback.Value.HistoryThresholdSeconds,
        playback.Value.HistoryRetentionEntries,
        storage.Value.MaxUploadBytes,
        storage.Value.MaxImageUploadBytes,
        AvailableQualities(),
        transcoder.IsAvailable,
        jwt.Value.AccessTokenMinutes));

    private IReadOnlyList<AudioQualityDto> AvailableQualities()
    {
        var qualities = new List<AudioQualityDto>();

        if (transcoder.IsAvailable)
        {
            foreach (var quality in (AudioQuality[])[AudioQuality.Low, AudioQuality.Normal, AudioQuality.High])
                qualities.Add(new AudioQualityDto(quality, transcode.Value.BitrateFor(quality)));
        }

        qualities.Add(new AudioQualityDto(AudioQuality.Original, null));
        return qualities;
    }
}

public record AudioQualityDto(AudioQuality Quality, int? BitrateKbps);

public record ClientConfigDto(
    int HistoryThresholdSeconds,
    int HistoryRetentionEntries,
    long MaxUploadBytes,
    long MaxImageUploadBytes,
    IReadOnlyList<AudioQualityDto> AudioQualities,

    // Без ffmpeg адаптивной раздачи нет вовсе, и клиенту незачем спрашивать master.m3u8.
    bool HlsEnabled,

    // Клиент продлевает сессию по таймеру: во время непрерывного воспроизведения запросов к API
    // нет, и без этого токен молча истекает прямо посреди трека.
    int AccessTokenMinutes);

