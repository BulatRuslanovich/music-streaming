// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Entities.Recommendations;

namespace Infrastructure.Persistence.Configurations;

public class PlaybackEventConfiguration : IEntityTypeConfiguration<PlaybackEvent>
{
    public void Configure(EntityTypeBuilder<PlaybackEvent> builder)
    {
        builder.Property(e => e.Sequence).UseIdentityByDefaultColumn();

        builder.HasOne(e => e.Track).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserTrackAffinityConfiguration : IEntityTypeConfiguration<UserTrackAffinity>
{
    public void Configure(EntityTypeBuilder<UserTrackAffinity> builder)
    {
        builder.ToTable("user_track_affinity");
        builder.HasKey(a => new { a.UserId, a.TrackId });
    }
}

public class UserArtistAffinityConfiguration : IEntityTypeConfiguration<UserArtistAffinity>
{
    public void Configure(EntityTypeBuilder<UserArtistAffinity> builder)
    {
        builder.ToTable("user_artist_affinity");
        builder.HasKey(a => new { a.UserId, a.ArtistId });
    }
}

public class UserGenreAffinityConfiguration : IEntityTypeConfiguration<UserGenreAffinity>
{
    public void Configure(EntityTypeBuilder<UserGenreAffinity> builder)
    {
        builder.ToTable("user_genre_affinity");
        builder.HasKey(a => new { a.UserId, a.GenreId });
    }
}

public class UserTasteProfileConfiguration : IEntityTypeConfiguration<UserTasteProfile>
{
    public void Configure(EntityTypeBuilder<UserTasteProfile> builder)
    {
        builder.HasKey(p => p.UserId);
        builder.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId);
    }
}

internal static class FloatArrays
{
    public static readonly ValueComparer<float[]> ByReference = new(
        (left, right) => ReferenceEquals(left, right),
        value => value.Length,
        value => value);
}
