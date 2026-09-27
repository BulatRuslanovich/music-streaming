// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

// Артист, на которого ссылаются альбомы, треки или кредиты, не удаляется вместе с ними: по
// умолчанию обязательная связь каскадная, и удалённый из контекста артист увёл бы за собой
// загруженные треки. Осиротевших артистов чистит отдельный проход обслуживания.
public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder) =>
        builder.HasOne(a => a.Artist).WithMany(a => a.Albums).OnDelete(DeleteBehavior.Restrict);
}

public class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder) =>
        builder.HasOne(t => t.Artist).WithMany(a => a.Tracks).OnDelete(DeleteBehavior.Restrict);
}

public class TrackArtistConfiguration : IEntityTypeConfiguration<TrackArtist>
{
    public void Configure(EntityTypeBuilder<TrackArtist> builder)
    {
        builder.HasKey(ta => new { ta.TrackId, ta.ArtistId });
        builder.HasOne(ta => ta.Artist).WithMany(a => a.TrackCredits).OnDelete(DeleteBehavior.Restrict);
    }
}
