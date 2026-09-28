// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

/// <summary>Largest files the API accepts.</summary>
/// <remarks>
/// Константы, а не настройки: как и <see cref="SecurityLimits"/>, они ни разу не менялись. Прокси
/// режет тело запроса раньше API (<c>max_size</c> в deploy/Caddyfile), поэтому его предел обязан
/// оставаться выше <see cref="AudioBytes"/> с запасом на multipart-обвязку.
/// </remarks>
public static class UploadLimits
{
    /// <summary>Largest accepted audio file.</summary>
    public const long AudioBytes = 200L * 1024 * 1024;

    /// <summary>Largest accepted cover or artist photo, uploaded or downloaded.</summary>
    public const long ImageBytes = 8L * 1024 * 1024;
}
