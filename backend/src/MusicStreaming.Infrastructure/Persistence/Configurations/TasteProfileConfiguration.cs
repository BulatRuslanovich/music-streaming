// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public class PlaybackEventConfiguration : IEntityTypeConfiguration<PlaybackEvent>
{
    public void Configure(EntityTypeBuilder<PlaybackEvent> builder)
    {
        builder.Property(e => e.Sequence).UseIdentityByDefaultColumn();

        // Трек у события необязателен, а по умолчанию EF на необязательной связи обнулил бы
        // ссылку вместо удаления: события удалённого трека уходят вместе с ним.
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

        builder.Property(p => p.TopArtists)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<TasteEntry>(), JsonColumn.Comparer<TasteEntry>());

        builder.Property(p => p.TopGenres)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<TasteEntry>(), JsonColumn.Comparer<TasteEntry>());
    }
}

public class UserTasteVectorConfiguration : IEntityTypeConfiguration<UserTasteVector>
{
    public void Configure(EntityTypeBuilder<UserTasteVector> builder)
    {
        builder.HasKey(vector => vector.UserId);
        builder.HasOne(vector => vector.User).WithMany().HasForeignKey(vector => vector.UserId);

        // Тот же компаратор по ссылке, что и у эмбеддингов треков: поэлементное сравнение
        // 512 float на каждом SaveChanges обошлось бы дороже самой записи.
        builder.Property(vector => vector.Vector).Metadata.SetValueComparer(FloatArrays.ByReference);
    }
}

/// <summary>Comparer for embedding-sized float arrays.</summary>
internal static class FloatArrays
{
    public static readonly ValueComparer<float[]> ByReference = new(
        (left, right) => ReferenceEquals(left, right),
        value => value.Length,
        value => value);
}
