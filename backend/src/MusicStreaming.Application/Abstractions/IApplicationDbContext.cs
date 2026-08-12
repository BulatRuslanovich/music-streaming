using Microsoft.EntityFrameworkCore;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

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

    DbSet<PlaybackEvent> PlaybackEvents { get; }
    DbSet<UserTrackAffinity> UserTrackAffinities { get; }
    DbSet<UserArtistAffinity> UserArtistAffinities { get; }
    DbSet<UserGenreAffinity> UserGenreAffinities { get; }
    DbSet<UserTasteProfile> UserTasteProfiles { get; }
    DbSet<TrackStats> TrackStats { get; }
    DbSet<TrackSimilarity> TrackSimilarities { get; }
    DbSet<RecommendationCacheEntry> RecommendationCache { get; }
    DbSet<RecommendationImpression> RecommendationImpressions { get; }
    DbSet<RecommendationRun> RecommendationRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
