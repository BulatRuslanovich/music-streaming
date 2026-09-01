// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities;

public class Album
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public int? Year { get; set; }
    public string? CoverPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<Track> Tracks { get; set; } = [];
}
