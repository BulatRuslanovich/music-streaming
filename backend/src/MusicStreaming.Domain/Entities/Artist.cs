// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities;

public class Artist
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? ImagePath { get; set; }

    /// <summary>Когда теги последний раз запрашивались у провайдера. null — ещё ни разу.</summary>
    public DateTimeOffset? TagsFetchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<Album> Albums { get; set; } = [];
    public ICollection<Track> Tracks { get; set; } = [];
    public ICollection<TrackArtist> TrackCredits { get; set; } = [];
    public ICollection<Recommendations.ArtistTag> Tags { get; set; } = [];
}
