// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Integrations;
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
    public DbSet<TrackStats> TrackStats => Set<TrackStats>();
    public DbSet<TrackAudioFeatures> TrackAudioFeatures => Set<TrackAudioFeatures>();
    public DbSet<TrackSimilarity> TrackSimilarities => Set<TrackSimilarity>();
    public DbSet<TrackSimilarityState> TrackSimilarityStates => Set<TrackSimilarityState>();
    public DbSet<RecommendationCacheEntry> RecommendationCache => Set<RecommendationCacheEntry>();
    public DbSet<RecommendationImpression> RecommendationImpressions => Set<RecommendationImpression>();
    public DbSet<RecommendationSuppression> RecommendationSuppressions => Set<RecommendationSuppression>();
    public DbSet<ArtistTag> ArtistTags => Set<ArtistTag>();
    public DbSet<TrackTag> TrackTags => Set<TrackTag>();
    public DbSet<RecommendationRun> RecommendationRuns => Set<RecommendationRun>();

    public DbSet<LastfmAccount> LastfmAccounts => Set<LastfmAccount>();
    public DbSet<OutboundJob> OutboundJobs => Set<OutboundJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder
            .HasDbFunction(typeof(SearchRank).GetMethod(nameof(SearchRank.Of))!)
            .HasName(SearchRank.FunctionName);

        modelBuilder.Entity<DailyActivityRow>(row =>
        {
            row.HasNoKey();
            row.ToTable(table => table.ExcludeFromMigrations());
        });

        modelBuilder.Entity<HourlyActivityRow>(row =>
        {
            row.HasNoKey();
            row.ToTable(table => table.ExcludeFromMigrations());
        });

        modelBuilder.Entity<LibraryStatsRow>(row =>
        {
            row.HasNoKey();
            row.ToTable(table => table.ExcludeFromMigrations());
        });

        modelBuilder.Entity<GenreCoverRow>(row =>
        {
            row.HasNoKey();
            row.ToTable(table => table.ExcludeFromMigrations());
        });

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(512);
        base.ConfigureConventions(configurationBuilder);
    }
}
