// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

public class TrackEmbeddingConfiguration : IEntityTypeConfiguration<TrackEmbedding>
{
    public void Configure(EntityTypeBuilder<TrackEmbedding> builder)
    {
        builder.ToTable("track_embeddings");
        builder.HasKey(embedding => embedding.TrackId);

        builder.HasOne(embedding => embedding.Track)
            .WithOne(track => track.Embedding)
            .HasForeignKey<TrackEmbedding>(embedding => embedding.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(embedding => embedding.ModelId).HasMaxLength(64);
        builder.Property(embedding => embedding.Strategy).HasMaxLength(32);
        builder.Property(embedding => embedding.SourceHash).HasMaxLength(64);
        builder.Property(embedding => embedding.Error).HasMaxLength(512);

        // Явный компаратор: без него EF сравнивает 512-элементный массив поэлементно на каждом
        // SaveChanges. Загрузчик индекса всё равно читает через AsNoTracking().Select(...),
        // так что этот путь горячим быть не должен — но цена ошибки слишком велика.
        builder.Property(embedding => embedding.Vector)
            .HasColumnType("real[]")
            .Metadata.SetValueComparer(new ValueComparer<float[]>(
                (left, right) => ReferenceEquals(left, right),
                vector => vector.Length,
                vector => vector));

        // Скан бэкфилла: "что ещё не посчитано этой моделью и этой стратегией".
        builder.HasIndex(embedding => new { embedding.Succeeded, embedding.ModelId, embedding.Strategy });
        builder.HasIndex(embedding => embedding.ClusterId);
        builder.HasIndex(embedding => embedding.AnalyzedAt);
    }
}

public class TrackTransitionConfiguration : IEntityTypeConfiguration<TrackTransition>
{
    public void Configure(EntityTypeBuilder<TrackTransition> builder)
    {
        builder.ToTable("track_transitions");
        builder.HasKey(transition => new { transition.FromTrackId, transition.ToTrackId });

        // Ведущая колонка ключа уже покрывает выборку «куда уходят от этого трека»,
        // поэтому отдельный индекс нужен только для обратного направления.
        builder.HasOne(transition => transition.FromTrack)
            .WithMany()
            .HasForeignKey(transition => transition.FromTrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(transition => transition.ToTrack)
            .WithMany()
            .HasForeignKey(transition => transition.ToTrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(transition => transition.ToTrackId);
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
