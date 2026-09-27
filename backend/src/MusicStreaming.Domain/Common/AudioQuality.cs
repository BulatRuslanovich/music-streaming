// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Common;

public enum AudioQuality
{
    Low = 0,
    Normal = 1,
    High = 2,
    Original = 3,
}

/// <summary>Bitrate of each transcoded quality step, in kbps.</summary>
/// <remarks>
/// Константы, а не настройка: лестница 64/128/192 ни разу не менялась, а HLS-рендишены на диске
/// нарезаны именно под неё — сдвиг числа без пересборки кэша отдавал бы старые файлы под новой
/// подписью.
/// </remarks>
public static class AudioBitrates
{
    public const int LowKbps = 64;
    public const int NormalKbps = 128;
    public const int HighKbps = 192;

    /// <summary>null for <see cref="AudioQuality.Original"/>: the original is never transcoded.</summary>
    public static int? For(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => LowKbps,
        AudioQuality.Normal => NormalKbps,
        AudioQuality.High => HighKbps,
        _ => null,
    };
}
