// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public class UserTasteVector
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public float[] Vector { get; set; } = [];

    public int Dimension { get; set; }

    public int PositiveCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
