// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

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

public class DailyMixSnapshotConfiguration : IEntityTypeConfiguration<DailyMixSnapshot>
{
    public void Configure(EntityTypeBuilder<DailyMixSnapshot> builder)
    {
        builder.ToTable("daily_mixes");
        builder.HasKey(m => new { m.UserId, m.LocalDate });

        builder.Property(m => m.TrackIds)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<Guid>(), JsonColumn.Comparer<Guid>());

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
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
