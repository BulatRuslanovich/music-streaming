// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<IReadOnlyList<T>, string> Converter<T>() => new(
        list => JsonSerializer.Serialize(list, Options),
        json => JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>());

    public static ValueComparer<IReadOnlyList<T>> Comparer<T>() => new(
        (left, right) => left != null && right != null ? left.SequenceEqual(right) : left == right,
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        list => list.ToList());
}

public class PlaybackEventConfiguration : IEntityTypeConfiguration<PlaybackEvent>
{
    public void Configure(EntityTypeBuilder<PlaybackEvent> builder)
    {
        builder.ToTable("playback_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Platform).HasMaxLength(32).IsRequired();

        builder.Property(e => e.Sequence).UseIdentityByDefaultColumn();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Track)
            .WithMany()
            .HasForeignKey(e => e.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.UserId, e.Sequence });
        builder.HasIndex(e => new { e.UserId, e.OccurredAt });
        builder.HasIndex(e => new { e.UserId, e.TrackId, e.OccurredAt });
        builder.HasIndex(e => new { e.SessionId, e.OccurredAt });

        builder.HasIndex(e => e.OccurredAt);
    }
}

public class UserTrackAffinityConfiguration : IEntityTypeConfiguration<UserTrackAffinity>
{
    public void Configure(EntityTypeBuilder<UserTrackAffinity> builder)
    {
        builder.ToTable("user_track_affinity");
        builder.HasKey(a => new { a.UserId, a.TrackId });

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Track)
            .WithMany()
            .HasForeignKey(a => a.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.UserId, a.Score });
        builder.HasIndex(a => new { a.UserId, a.LastPlayedAt });

        builder.HasIndex(a => a.TrackId);
    }
}

public class UserArtistAffinityConfiguration : IEntityTypeConfiguration<UserArtistAffinity>
{
    public void Configure(EntityTypeBuilder<UserArtistAffinity> builder)
    {
        builder.ToTable("user_artist_affinity");
        builder.HasKey(a => new { a.UserId, a.ArtistId });

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Artist)
            .WithMany()
            .HasForeignKey(a => a.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.UserId, a.Score });
    }
}

public class UserGenreAffinityConfiguration : IEntityTypeConfiguration<UserGenreAffinity>
{
    public void Configure(EntityTypeBuilder<UserGenreAffinity> builder)
    {
        builder.ToTable("user_genre_affinity");
        builder.HasKey(a => new { a.UserId, a.GenreId });

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Genre)
            .WithMany()
            .HasForeignKey(a => a.GenreId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.UserId, a.Score });
    }
}

public class UserTasteProfileConfiguration : IEntityTypeConfiguration<UserTasteProfile>
{
    public void Configure(EntityTypeBuilder<UserTasteProfile> builder)
    {
        builder.ToTable("user_taste_profiles");
        builder.HasKey(p => p.UserId);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.TopArtists)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<TasteEntry>(), JsonColumn.Comparer<TasteEntry>());

        builder.Property(p => p.TopGenres)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<TasteEntry>(), JsonColumn.Comparer<TasteEntry>());

        builder.Property(p => p.Dayparts)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<DaypartTaste>(), JsonColumn.Comparer<DaypartTaste>());
    }
}

public class TrackStatsConfiguration : IEntityTypeConfiguration<TrackStats>
{
    public void Configure(EntityTypeBuilder<TrackStats> builder)
    {
        builder.ToTable("track_stats");
        builder.HasKey(s => s.TrackId);

        builder.HasOne(s => s.Track)
            .WithOne(t => t.Stats)
            .HasForeignKey<TrackStats>(s => s.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.PopularityScore);
    }
}

public class TrackAudioFeaturesConfiguration : IEntityTypeConfiguration<TrackAudioFeatures>
{
    public void Configure(EntityTypeBuilder<TrackAudioFeatures> builder)
    {
        builder.ToTable("track_audio_features");
        builder.HasKey(features => features.TrackId);

        builder.HasOne(features => features.Track)
            .WithOne(track => track.AudioFeatures)
            .HasForeignKey<TrackAudioFeatures>(features => features.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(features => features.Error).HasMaxLength(512);
        builder.HasIndex(features => new { features.Succeeded, features.AlgorithmVersion });
        builder.HasIndex(features => features.AnalyzedAt);
    }
}

public class TrackSimilarityConfiguration : IEntityTypeConfiguration<TrackSimilarity>
{
    public void Configure(EntityTypeBuilder<TrackSimilarity> builder)
    {
        builder.ToTable("track_similarity");
        builder.HasKey(s => new { s.TrackId, s.SimilarTrackId });

        builder.HasOne(s => s.Track)
            .WithMany()
            .HasForeignKey(s => s.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.SimilarTrack)
            .WithMany()
            .HasForeignKey(s => s.SimilarTrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TrackId, s.Score });
    }
}

public class TrackSimilarityStateConfiguration : IEntityTypeConfiguration<TrackSimilarityState>
{
    public void Configure(EntityTypeBuilder<TrackSimilarityState> builder)
    {
        builder.ToTable("track_similarity_state");
        builder.HasKey(s => s.TrackId);

        builder.Property(s => s.Fingerprint).HasMaxLength(32).IsRequired();

        builder.HasOne(s => s.Track)
            .WithMany()
            .HasForeignKey(s => s.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.ComputedAt);
    }
}

public class RecommendationCacheEntryConfiguration : IEntityTypeConfiguration<RecommendationCacheEntry>
{
    public void Configure(EntityTypeBuilder<RecommendationCacheEntry> builder)
    {
        builder.ToTable("recommendation_cache");
        builder.HasKey(c => new { c.UserId, c.ShelfKey });

        builder.Property(c => c.ShelfKey).HasMaxLength(120).IsRequired();

        builder.Property(c => c.Payload)
            .HasColumnType("jsonb")
            .HasConversion(
                JsonColumn.Converter<CachedRecommendation>(),
                JsonColumn.Comparer<CachedRecommendation>());

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.ExpiresAt);
        builder.HasIndex(c => new { c.UserId, c.Position });
    }
}

public class RecommendationImpressionConfiguration : IEntityTypeConfiguration<RecommendationImpression>
{
    public void Configure(EntityTypeBuilder<RecommendationImpression> builder)
    {
        builder.ToTable("recommendation_impressions");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ShelfKey).HasMaxLength(120).IsRequired();

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Track)
            .WithMany()
            .HasForeignKey(i => i.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.UserId, i.TrackId, i.ShownAt });
        builder.HasIndex(i => new { i.UserId, i.ShelfKey, i.ShownAt });
        builder.HasIndex(i => i.ShownAt);
    }
}

public class RecommendationRunConfiguration : IEntityTypeConfiguration<RecommendationRun>
{
    public void Configure(EntityTypeBuilder<RecommendationRun> builder)
    {
        builder.ToTable("recommendation_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Error).HasMaxLength(2000);

        builder.HasIndex(r => r.StartedAt);
        builder.HasIndex(r => new { r.UserId, r.StartedAt });
    }
}

public class RecommendationSuppressionConfiguration : IEntityTypeConfiguration<RecommendationSuppression>
{
    public void Configure(EntityTypeBuilder<RecommendationSuppression> builder)
    {
        builder.ToTable("recommendation_suppressions");
        builder.HasKey(s => s.Id);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Цель это либо трек, либо артист, поэтому внешнего ключа на неё нет: удаление цели
        // оставляет висячее подавление, которое никому не мешает и уходит вместе с чисткой.
        builder.HasIndex(s => new { s.UserId, s.Target, s.TargetId }).IsUnique();
        builder.HasIndex(s => s.ExpiresAt);
    }
}

public class ArtistTagConfiguration : IEntityTypeConfiguration<ArtistTag>
{
    public void Configure(EntityTypeBuilder<ArtistTag> builder)
    {
        builder.ToTable("artist_tags");
        builder.HasKey(t => new { t.ArtistId, t.Name });

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();

        builder.HasOne(t => t.Artist)
            .WithMany(a => a.Tags)
            .HasForeignKey(t => t.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.Name);
    }
}

public class TrackTagConfiguration : IEntityTypeConfiguration<TrackTag>
{
    public void Configure(EntityTypeBuilder<TrackTag> builder)
    {
        builder.ToTable("track_tags");
        builder.HasKey(t => new { t.TrackId, t.Name });

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();

        builder.HasOne(t => t.Track)
            .WithMany(track => track.Tags)
            .HasForeignKey(t => t.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.Name);
    }
}
