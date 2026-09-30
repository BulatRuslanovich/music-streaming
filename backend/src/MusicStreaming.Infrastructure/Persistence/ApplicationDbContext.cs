// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<TrackLyrics> TrackLyrics => Set<TrackLyrics>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ListeningHistoryEntry> ListeningHistory => Set<ListeningHistoryEntry>();
    public DbSet<ListeningStat> ListeningStats => Set<ListeningStat>();

    public DbSet<PlaybackEvent> PlaybackEvents => Set<PlaybackEvent>();
    public DbSet<UserTrackAffinity> UserTrackAffinities => Set<UserTrackAffinity>();
    public DbSet<UserArtistAffinity> UserArtistAffinities => Set<UserArtistAffinity>();
    public DbSet<UserGenreAffinity> UserGenreAffinities => Set<UserGenreAffinity>();
    public DbSet<UserTasteProfile> UserTasteProfiles => Set<UserTasteProfile>();
    public DbSet<UserTasteVector> UserTasteVectors => Set<UserTasteVector>();
    public DbSet<TrackStats> TrackStats => Set<TrackStats>();
    public DbSet<TrackEmbedding> TrackEmbeddings => Set<TrackEmbedding>();
    public DbSet<TrackTransition> TrackTransitions => Set<TrackTransition>();
    public DbSet<RecommendationCacheEntry> RecommendationCache => Set<RecommendationCacheEntry>();
    public DbSet<DailyMixSnapshot> DailyMixes => Set<DailyMixSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder
            .HasDbFunction(typeof(SearchRank).GetMethod(nameof(SearchRank.Of))!)
            .HasName(SearchRank.FunctionName);

        modelBuilder.Entity<LibraryStatsRow>().HasNoKey();
        modelBuilder.Entity<GenreCoverRow>().HasNoKey();

        base.OnModelCreating(modelBuilder);
    }
}
