// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public record TasteEntry(Guid Id, string Name, double Score);

public class UserTasteProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public double PositiveSignalMass { get; set; }
    public DateTimeOffset SignalDecayAnchor { get; set; }
    public double? YearCenter { get; set; }
    public double YearSpread { get; set; }
    public IReadOnlyList<TasteEntry> TopArtists { get; set; } = [];

    public ProfileMaturity Maturity { get; set; }
    public long EventsWatermark { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
