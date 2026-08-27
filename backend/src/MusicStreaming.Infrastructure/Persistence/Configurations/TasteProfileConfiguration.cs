// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

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
