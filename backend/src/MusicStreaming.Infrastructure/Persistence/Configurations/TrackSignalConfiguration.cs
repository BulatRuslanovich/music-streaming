// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

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
