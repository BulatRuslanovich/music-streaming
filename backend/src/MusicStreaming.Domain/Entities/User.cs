// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public UserSettings? Settings { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Playlist> Playlists { get; set; } = [];
    public ICollection<Favorite> Favorites { get; set; } = [];
    public ICollection<ListeningHistoryEntry> History { get; set; } = [];
}
