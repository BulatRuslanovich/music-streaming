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
        builder.HasKey(s => s.TrackId);
        builder.HasOne(s => s.Track).WithOne(t => t.Stats).HasForeignKey<TrackStats>(s => s.TrackId);
    }
}

public class TrackEmbeddingConfiguration : IEntityTypeConfiguration<TrackEmbedding>
{
    public void Configure(EntityTypeBuilder<TrackEmbedding> builder)
    {
        builder.HasKey(embedding => embedding.TrackId);
        builder.HasOne(embedding => embedding.Track)
            .WithOne(track => track.Embedding)
            .HasForeignKey<TrackEmbedding>(embedding => embedding.TrackId);

        builder.Property(embedding => embedding.Vector).Metadata.SetValueComparer(FloatArrays.ByReference);
    }
}

public class TrackTransitionConfiguration : IEntityTypeConfiguration<TrackTransition>
{
    public void Configure(EntityTypeBuilder<TrackTransition> builder) =>
        builder.HasKey(transition => new { transition.FromTrackId, transition.ToTrackId });
}
