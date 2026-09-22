// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public static OptionsBuilder<PlaybackOptions> Validated(OptionsBuilder<PlaybackOptions> builder) => builder
        .Validate(o => o.HistoryThresholdSeconds > 0, "Playback:HistoryThresholdSeconds must be greater than zero.");
}
