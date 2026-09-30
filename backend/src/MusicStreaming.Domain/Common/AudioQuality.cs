// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Common;

public enum AudioQuality
{
    Low = 0,
    Normal = 1,
    Original = 3,
}

public static class AudioBitrates
{
    public const int LowKbps = 64;
    public const int NormalKbps = 128;

    public static int? For(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => LowKbps,
        AudioQuality.Normal => NormalKbps,
        _ => null,
    };
}
