// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public record TasteEntry(Guid Id, string Name, double Score);

public class UserTasteProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int PositiveSignalCount { get; set; }

    /// <summary>Затухающая масса положительных сигналов — то, по чему считается зрелость.</summary>
    public double PositiveSignalMass { get; set; }
    public DateTimeOffset SignalDecayAnchor { get; set; }
    public int TotalEventCount { get; set; }
    public long TotalListeningSeconds { get; set; }
    public double AverageCompletion { get; set; }
    public double SkipRate { get; set; }
    public int DistinctTracks { get; set; }
    public int DistinctArtists { get; set; }
    public double? YearCenter { get; set; }
    public double YearSpread { get; set; }
    public IReadOnlyList<TasteEntry> TopArtists { get; set; } = [];
    public IReadOnlyList<TasteEntry> TopGenres { get; set; } = [];

    /// <summary>Вкус по частям суток; пуст, пока слушать было нечего или слишком мало.</summary>
    public IReadOnlyList<DaypartTaste> Dayparts { get; set; } = [];
    public ProfileMaturity Maturity { get; set; }
    public long EventsWatermark { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
