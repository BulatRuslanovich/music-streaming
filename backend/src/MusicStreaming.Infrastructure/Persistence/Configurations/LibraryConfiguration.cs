using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        builder.ToTable("artists");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(300).IsRequired();
        builder.Property(a => a.NormalizedName).HasMaxLength(300).IsRequired();
        builder.Property(a => a.ImagePath).HasMaxLength(400);

        builder.HasIndex(a => a.NormalizedName).IsUnique();
        builder.HasIndex(a => a.Name);
    }
}

public class AlbumConfiguration : IEntityTypeConfiguration<Album>
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

        builder.HasIndex(a => new { a.ArtistId, a.NormalizedTitle }).IsUnique();
        builder.HasIndex(a => a.Title);
        builder.HasIndex(a => a.CreatedAt);
    }
}

public class GenreConfiguration : IEntityTypeConfiguration<Genre>
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

public class TrackConfiguration : IEntityTypeConfiguration<Track>
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

        builder.HasIndex(t => t.ContentHash).IsUnique();
        builder.HasIndex(t => t.FilePath).IsUnique();
    }
}

public class TrackArtistConfiguration : IEntityTypeConfiguration<TrackArtist>
{
    public void Configure(EntityTypeBuilder<TrackArtist> builder)
    {
        builder.ToTable("track_artists");

        builder.HasKey(ta => new { ta.TrackId, ta.ArtistId });

        builder.HasOne(ta => ta.Track)
            .WithMany(t => t.TrackArtists)
            .HasForeignKey(ta => ta.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ta => ta.Artist)
            .WithMany(a => a.TrackCredits)
            .HasForeignKey(ta => ta.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ta => ta.ArtistId);

        builder.HasIndex(ta => new { ta.TrackId, ta.Position });
    }
}
