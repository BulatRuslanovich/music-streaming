using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.ToTable("playlists");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.CoverPath).HasMaxLength(400);

        builder.HasOne(p => p.User)
            .WithMany(u => u.Playlists)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.UserId, p.Name });
        builder.HasIndex(p => p.CreatedAt);

        builder.HasIndex(p => new { p.IsPublic, p.UpdatedAt });
    }
}

public class PlaylistTrackConfiguration : IEntityTypeConfiguration<PlaylistTrack>
{
    public void Configure(EntityTypeBuilder<PlaylistTrack> builder)
    {
        builder.ToTable("playlist_tracks");
        builder.HasKey(pt => pt.Id);

        builder.HasOne(pt => pt.Playlist)
            .WithMany(p => p.Tracks)
            .HasForeignKey(pt => pt.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pt => pt.Track)
            .WithMany(t => t.PlaylistTracks)
            .HasForeignKey(pt => pt.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pt => new { pt.PlaylistId, pt.Position });
        builder.HasIndex(pt => pt.TrackId);

        builder.HasIndex(pt => new { pt.PlaylistId, pt.TrackId }).IsUnique();
    }
}

public class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("favorites");

        builder.HasKey(f => new { f.UserId, f.TrackId });

        builder.HasOne(f => f.User)
            .WithMany(u => u.Favorites)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Track)
            .WithMany(t => t.Favorites)
            .HasForeignKey(f => f.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => new { f.UserId, f.CreatedAt });
    }
}

public class ListeningHistoryConfiguration : IEntityTypeConfiguration<ListeningHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ListeningHistoryEntry> builder)
    {
        builder.ToTable("listening_history");
        builder.HasKey(h => h.Id);

        builder.HasOne(h => h.User)
            .WithMany(u => u.History)
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Track)
            .WithMany(t => t.History)
            .HasForeignKey(h => h.TrackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => h.PlayedAt);
        builder.HasIndex(h => new { h.UserId, h.PlayedAt });
        builder.HasIndex(h => new { h.UserId, h.TrackId, h.PlayedAt });
    }
}
