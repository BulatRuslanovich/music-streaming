// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Integrations;

public enum OutboundJobKind
{
    LastfmNowPlaying = 1,
    LastfmScrobble = 2,
}

public enum OutboundJobState
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

public class OutboundJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public OutboundJobKind Kind { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Payload { get; set; } = "{}";
    public string DedupeKey { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public OutboundJobState State { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
