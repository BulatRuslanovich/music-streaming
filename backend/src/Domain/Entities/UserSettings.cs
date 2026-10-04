// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Common;

namespace Domain.Entities;

public class UserSettings
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public AudioQuality Quality { get; set; } = AudioQuality.Normal;
    public string TimeZone { get; set; } = "UTC";
    public DateTimeOffset UpdatedAt { get; set; }
}
