// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Entities;

namespace Infrastructure.Persistence.Configurations;

public class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.HasKey(s => s.UserId);
        builder.HasOne(s => s.User).WithOne(u => u.Settings).HasForeignKey<UserSettings>(s => s.UserId);
    }
}

public class TrackLyricsConfiguration : IEntityTypeConfiguration<TrackLyrics>
{
    public void Configure(EntityTypeBuilder<TrackLyrics> builder)
    {
        builder.HasKey(l => l.TrackId);
        builder.HasOne(l => l.Track).WithOne(t => t.Lyrics).HasForeignKey<TrackLyrics>(l => l.TrackId);

        builder.Property(l => l.Synced)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<LyricLine>(), JsonColumn.Comparer<LyricLine>());
    }
}

public class ListeningStatConfiguration : IEntityTypeConfiguration<ListeningStat>
{
    public void Configure(EntityTypeBuilder<ListeningStat> builder) =>
        builder.HasKey(s => new { s.UserId, s.Hour, s.TrackId });
}
