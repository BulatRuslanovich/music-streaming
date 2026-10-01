// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public class TrackTransition
{
    public Guid FromTrackId { get; set; }
    public Guid ToTrackId { get; set; }

    public double Weight { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
