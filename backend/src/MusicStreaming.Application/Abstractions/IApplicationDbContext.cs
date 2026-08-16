using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Integrations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Artist> Artists { get; }
    DbSet<Album> Albums { get; }
    DbSet<Genre> Genres { get; }
    DbSet<Track> Tracks { get; }
    DbSet<TrackArtist> TrackArtists { get; }
    DbSet<TrackLyrics> TrackLyrics { get; }
    DbSet<Playlist> Playlists { get; }
    DbSet<PlaylistTrack> PlaylistTracks { get; }
    DbSet<Favorite> Favorites { get; }
    DbSet<ListeningHistoryEntry> ListeningHistory { get; }
    DbSet<ListeningStat> ListeningStats { get; }

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

    DbSet<LastfmAccount> LastfmAccounts { get; }
    DbSet<OutboundJob> OutboundJobs { get; }

    /// <summary>
    /// Нужен там, где ответ по-настоящему даёт SQL: агрегации статистики разворачивают часы в
    /// местные сутки через <c>AT TIME ZONE</c>, а ранжирование поиска считается выражением, которого
    /// в LINQ нет.
    /// </summary>
    DatabaseFacade Database { get; }

    /// <summary>Нужен для результатов сырых запросов, у которых нет своей таблицы, — см. <c>StatisticsService</c>.</summary>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    /// <summary>
    /// Нужен там, где одна неудача не должна утащить за собой соседей: загрузка пакета файлов
    /// продолжается после отвергнутого файла, и незаписанное им приходится забывать явно, иначе
    /// оно уедет в базу вместе со следующим. См. <c>TrackUploadService</c>.
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
