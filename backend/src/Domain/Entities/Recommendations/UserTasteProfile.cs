// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

// Состояние свёртки событий: до какого события свёрнуто и сколько положительных сигналов накоплено.
public class UserTasteProfile
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public double PositiveSignalMass { get; set; }
    public DateTimeOffset SignalDecayAnchor { get; set; }
    public long EventsWatermark { get; set; }
}
