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
