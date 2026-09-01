// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Dtos;

public record UserSettingsDto(bool Autoplay, AudioQuality Quality, bool DataSaver, string TimeZone);

public record UpdateUserSettingsRequest(
    bool? Autoplay,
    AudioQuality? Quality,
    bool? DataSaver,
    string? TimeZone);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record AudioQualityDto(AudioQuality Quality, int? BitrateKbps);

/// <summary>Настройки, которые фронтенд забирает один раз при старте.</summary>
public record ClientConfigDto(
    int HistoryThresholdSeconds,
    long MaxUploadBytes,
    long MaxImageUploadBytes,
    IReadOnlyList<AudioQualityDto> AudioQualities,
    bool HlsEnabled,
    int AccessTokenMinutes);
