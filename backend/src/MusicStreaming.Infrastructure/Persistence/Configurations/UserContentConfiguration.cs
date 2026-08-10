using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

public sealed class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
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
    }
}

public sealed class PlaylistTrackConfiguration : IEntityTypeConfiguration<PlaylistTrack>
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

        // Deliberately non-unique: a playlist may repeat a track, and a reorder rewrites many
        // positions in one SaveChanges, which a unique constraint would trip over mid-update.
        builder.HasIndex(pt => new { pt.PlaylistId, pt.Position });
        builder.HasIndex(pt => pt.TrackId);
    }
}

public sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("favorites");

        // The pair is the identity of a favourite, so it needs no surrogate key.
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

public sealed class ListeningHistoryConfiguration : IEntityTypeConfiguration<ListeningHistoryEntry>
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
