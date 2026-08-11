using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public sealed class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.ToTable("artists");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(300).IsRequired();
        builder.Property(a => a.NormalizedName).HasMaxLength(300).IsRequired();

        builder.HasIndex(a => a.NormalizedName).IsUnique();
        builder.HasIndex(a => a.Name);
    }
}

public sealed class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("albums");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).HasMaxLength(300).IsRequired();
        builder.Property(a => a.NormalizedTitle).HasMaxLength(300).IsRequired();
        builder.Property(a => a.CoverPath).HasMaxLength(400);

        builder.HasOne(a => a.Artist)
            .WithMany(a => a.Albums)
            .HasForeignKey(a => a.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        // One album title per artist; the same title by a different artist is a different album.
        builder.HasIndex(a => new { a.ArtistId, a.NormalizedTitle }).IsUnique();
        builder.HasIndex(a => a.Title);
        builder.HasIndex(a => a.CreatedAt);
    }
}

public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("genres");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).HasMaxLength(150).IsRequired();
        builder.Property(g => g.NormalizedName).HasMaxLength(150).IsRequired();

        builder.HasIndex(g => g.NormalizedName).IsUnique();
    }
}

public sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> builder)
    {
        builder.ToTable("tracks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(400).IsRequired();
        builder.Property(t => t.NormalizedTitle).HasMaxLength(400).IsRequired();
        builder.Property(t => t.FilePath).HasMaxLength(400).IsRequired();
        builder.Property(t => t.OriginalFileName).HasMaxLength(400).IsRequired();
        builder.Property(t => t.MimeType).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ContentHash).HasMaxLength(64).IsRequired();

        builder.HasOne(t => t.Artist)
            .WithMany(a => a.Tracks)
            .HasForeignKey(t => t.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Album)
            .WithMany(a => a.Tracks)
            .HasForeignKey(t => t.AlbumId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Genre)
            .WithMany(g => g.Tracks)
            .HasForeignKey(t => t.GenreId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => t.ArtistId);
        builder.HasIndex(t => t.AlbumId);
        builder.HasIndex(t => t.GenreId);
        builder.HasIndex(t => t.Title);
        builder.HasIndex(t => t.CreatedAt);

        // Rejects a byte-identical re-upload of a file that is already in the library.
        builder.HasIndex(t => t.ContentHash).IsUnique();

        // The stored location is unique by construction; the index makes that a guarantee.
        builder.HasIndex(t => t.FilePath).IsUnique();
    }
}

public sealed class TrackArtistConfiguration : IEntityTypeConfiguration<TrackArtist>
{
    public void Configure(EntityTypeBuilder<TrackArtist> builder)
    {
        builder.ToTable("track_artists");

        // The pair is the identity: an artist is credited on a track once, whatever the order.
        builder.HasKey(ta => new { ta.TrackId, ta.ArtistId });

        builder.HasOne(ta => ta.Track)
            .WithMany(t => t.TrackArtists)
            .HasForeignKey(ta => ta.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict mirrors tracks.artist_id: an artist row is removed by the orphan sweep only
        // once nothing credits it any more.
        builder.HasOne(ta => ta.Artist)
            .WithMany(a => a.TrackCredits)
            .HasForeignKey(ta => ta.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        // The key already covers lookups by track; this one serves the artist page.
        builder.HasIndex(ta => ta.ArtistId);

        // Position is deliberately not unique per track: re-ordering credits would otherwise
        // trip the constraint mid-statement.
        builder.HasIndex(ta => new { ta.TrackId, ta.Position });
    }
}
