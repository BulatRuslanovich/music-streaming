// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public class TrackStats
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int PlayCount { get; set; }
    public int SkipCount { get; set; }
    public double SkipRate { get; set; }
    public double PopularityScore { get; set; }

    /// <summary>
    /// How many times the track was shown in recommendations.
    /// </summary>
    /// <remarks>
    /// Кормит затухание new-boost по числу показов: свежий трек, который уже десять раз предложили
    /// и не послушали, перестаёт всплывать сам собой.
    /// </remarks>
    public int ShownCount { get; set; }

    /// <summary>How often the track was abandoned in its first 20% — a hard gate on the new-boost.</summary>
    public int SkippedEarlyCount { get; set; }

    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}
