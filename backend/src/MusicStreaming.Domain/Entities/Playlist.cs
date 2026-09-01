// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities;

public class Playlist
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverPath { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<PlaylistTrack> Tracks { get; set; } = [];
}
