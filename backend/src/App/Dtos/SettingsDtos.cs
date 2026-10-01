// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Common;

namespace App.Dtos;

public record UserSettingsDto(AudioQuality Quality, bool DataSaver, string TimeZone);

public record UpdateUserSettingsRequest(AudioQuality? Quality, bool? DataSaver, string? TimeZone);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ClientConfigDto(
    long MaxUploadBytes,
    long MaxImageUploadBytes,
    int AccessTokenMinutes);
