// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Настройки, которые клиенту нужны до первого запроса данных: пороги истории, лимиты загрузки и
/// список доступных качеств.
/// </summary>
/// <remarks>
/// Список качеств зависит от того, поднялся ли ffmpeg, поэтому он не константа конфигурации, а
/// ответ на вопрос «что сейчас можно отдать». Раньше он собирался прямо в контроллере.
/// </remarks>
public class ClientConfigService(
    IOptions<PlaybackOptions> playback,
    IOptions<StorageOptions> storage,
    IOptions<TranscodeOptions> transcode,
    IOptions<JwtOptions> jwt,
    IAudioTranscoder transcoder)
{
    public ClientConfigDto Get() => new(
        playback.Value.HistoryThresholdSeconds,
        storage.Value.MaxUploadBytes,
        storage.Value.MaxImageUploadBytes,
        AvailableQualities(),
        transcoder.IsAvailable,
        jwt.Value.AccessTokenMinutes);

    private IReadOnlyList<AudioQualityDto> AvailableQualities()
    {
        var qualities = new List<AudioQualityDto>();

        if (transcoder.IsAvailable)
        {
            foreach (var quality in (AudioQuality[])[AudioQuality.Low, AudioQuality.Normal, AudioQuality.High])
                qualities.Add(new AudioQualityDto(quality, transcode.Value.BitrateFor(quality)));
        }

        // Оригинал доступен всегда: это сам загруженный файл, для него ничего готовить не нужно.
        qualities.Add(new AudioQualityDto(AudioQuality.Original, null));
        return qualities;
    }
}
