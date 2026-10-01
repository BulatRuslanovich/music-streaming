// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public class DailyMixSnapshot
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateOnly LocalDate { get; set; }
    public IReadOnlyList<Guid> TrackIds { get; set; } = [];
}
