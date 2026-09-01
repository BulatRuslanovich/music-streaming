// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public class TrackStats
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int PlayCount { get; set; }
    public int PlayCount30d { get; set; }
    public int SkipCount { get; set; }
    public int DistinctListeners { get; set; }
    public double CompletionRate { get; set; }
    public double SkipRate { get; set; }
    public double PopularityScore { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

public class TrackSimilarity
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public Guid SimilarTrackId { get; set; }
    public Track? SimilarTrack { get; set; }
    public double Score { get; set; }
    public double ContentScore { get; set; }
    public double? AudioScore { get; set; }
    public double CollabScore { get; set; }
    public int Support { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>
/// Отпечаток входов, из которых считалась схожесть трека. Пересчёт сравнивает его с текущим
/// состоянием: совпал — трек трогать не нужно, разошёлся — трек и его окружение пересобираются.
/// </summary>
public class TrackSimilarityState
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Когда отпечаток был записан. Самый старый из них — это время последней полной пересборки.</summary>
    public DateTimeOffset ComputedAt { get; set; }
}
