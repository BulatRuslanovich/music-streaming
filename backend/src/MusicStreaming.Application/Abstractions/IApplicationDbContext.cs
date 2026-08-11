using Microsoft.EntityFrameworkCore;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Artist> Artists { get; }
    DbSet<Album> Albums { get; }
    DbSet<Genre> Genres { get; }
    DbSet<Track> Tracks { get; }
    DbSet<TrackArtist> TrackArtists { get; }
    DbSet<Playlist> Playlists { get; }
    DbSet<PlaylistTrack> PlaylistTracks { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<ListeningHistoryEntry> ListeningHistory { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
