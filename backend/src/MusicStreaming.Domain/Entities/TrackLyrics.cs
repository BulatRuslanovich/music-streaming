// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities;

public record LyricLine(int At, string Text);

public enum LyricsSource
{
    Embedded = 0,
    Manual = 1,
    Provider = 2,
}

public class TrackLyrics
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public string Plain { get; set; } = string.Empty;
    public IReadOnlyList<LyricLine> Synced { get; set; } = [];
    public LyricsSource Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsSynced => Synced.Count > 0;
}
