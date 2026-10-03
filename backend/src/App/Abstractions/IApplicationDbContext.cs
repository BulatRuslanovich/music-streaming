// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Domain.Entities;
using Domain.Entities.Recommendations;

namespace App.Abstractions;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Artist> Artists { get; }
    DbSet<Album> Albums { get; }
    DbSet<Genre> Genres { get; }
    DbSet<Track> Tracks { get; }
    DbSet<TrackArtist> TrackArtists { get; }
    DbSet<TrackLyrics> TrackLyrics { get; }
    DbSet<Playlist> Playlists { get; }
    DbSet<PlaylistTrack> PlaylistTracks { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<ListeningHistoryEntry> ListeningHistory { get; }

    DbSet<PlaybackEvent> PlaybackEvents { get; }
    DbSet<UserTrackAffinity> UserTrackAffinities { get; }
    DbSet<UserArtistAffinity> UserArtistAffinities { get; }
    DbSet<UserGenreAffinity> UserGenreAffinities { get; }
    DbSet<UserTasteProfile> UserTasteProfiles { get; }
    DbSet<TrackStats> TrackStats { get; }
    DbSet<TrackEmbedding> TrackEmbeddings { get; }
    DbSet<RecommendationCacheEntry> RecommendationCache { get; }
    DbSet<DailyMixSnapshot> DailyMixes { get; }


    DatabaseFacade Database { get; }
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
