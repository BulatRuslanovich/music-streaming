// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>A scored neighbour of a track: how alike two tracks are, and on what evidence.</summary>
public class TrackSimilarity
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public Guid SimilarTrackId { get; set; }
    public Track? SimilarTrack { get; set; }
    public double Score { get; set; }
    public double ContentScore { get; set; }
    public double CollabScore { get; set; }
    public int Support { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>
/// A fingerprint of the inputs a track's similarity was computed from.
/// </summary>
/// <remarks>
/// Пересчёт сравнивает его с текущим состоянием: совпал — трек трогать не нужно,
/// разошёлся — трек и его окружение пересобираются.
/// </remarks>
public class TrackSimilarityState
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>When the fingerprint was written. The oldest one marks the last full rebuild.</summary>
    public DateTimeOffset ComputedAt { get; set; }
}
