// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public class PlaybackEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long Sequence { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? TrackId { get; set; }
    public Track? Track { get; set; }
    public Guid? EntityId { get; set; }
    public PlaybackEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int PositionSeconds { get; set; }
    public int ListenedSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public Guid SessionId { get; set; }
    public PlaybackSource Source { get; set; }
    public Guid? SourceId { get; set; }
    public string Platform { get; set; } = "web";
}
