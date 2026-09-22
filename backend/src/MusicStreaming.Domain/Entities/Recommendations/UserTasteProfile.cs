// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public record TasteEntry(Guid Id, string Name, double Score);

public class UserTasteProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int PositiveSignalCount { get; set; }

    /// <summary>Decaying mass of positive signals — what maturity is computed from.</summary>
    public double PositiveSignalMass { get; set; }
    public DateTimeOffset SignalDecayAnchor { get; set; }
    public int TotalEventCount { get; set; }
    public double AverageCompletion { get; set; }
    public double SkipRate { get; set; }
    public int DistinctTracks { get; set; }
    public double? YearCenter { get; set; }
    public double YearSpread { get; set; }
    public IReadOnlyList<TasteEntry> TopArtists { get; set; } = [];
    public IReadOnlyList<TasteEntry> TopGenres { get; set; } = [];

    /// <summary>Taste per part of the day; empty until there is enough listening to shape it.</summary>
    public IReadOnlyList<DaypartTaste> Dayparts { get; set; } = [];
    public ProfileMaturity Maturity { get; set; }
    public long EventsWatermark { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
