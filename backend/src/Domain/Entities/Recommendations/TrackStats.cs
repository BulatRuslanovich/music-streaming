// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public class TrackStats
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int PlayCount { get; set; }
    public double SkipRate { get; set; }
    public double PopularityScore { get; set; }

    public int SkippedEarlyCount { get; set; }
}
