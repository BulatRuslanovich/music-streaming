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
        builder.HasKey(c => new { c.UserId, c.ShelfKey });

        builder.Property(c => c.Payload)
            .HasColumnType("jsonb")
            .HasConversion(
                JsonColumn.Converter<CachedRecommendation>(),
                JsonColumn.Comparer<CachedRecommendation>());
    }
}

public class DailyMixSnapshotConfiguration : IEntityTypeConfiguration<DailyMixSnapshot>
{
    public void Configure(EntityTypeBuilder<DailyMixSnapshot> builder)
    {
        builder.HasKey(m => new { m.UserId, m.LocalDate });

        builder.Property(m => m.TrackIds)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<Guid>(), JsonColumn.Comparer<Guid>());
    }
}
