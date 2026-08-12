using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<ListeningHistoryEntry> ListeningHistory => Set<ListeningHistoryEntry>();

    public DbSet<PlaybackEvent> PlaybackEvents => Set<PlaybackEvent>();
    public DbSet<UserTrackAffinity> UserTrackAffinities => Set<UserTrackAffinity>();
    public DbSet<UserArtistAffinity> UserArtistAffinities => Set<UserArtistAffinity>();
    public DbSet<UserGenreAffinity> UserGenreAffinities => Set<UserGenreAffinity>();
    public DbSet<UserTasteProfile> UserTasteProfiles => Set<UserTasteProfile>();
    public DbSet<TrackStats> TrackStats => Set<TrackStats>();
    public DbSet<TrackSimilarity> TrackSimilarities => Set<TrackSimilarity>();
    public DbSet<RecommendationCacheEntry> RecommendationCache => Set<RecommendationCacheEntry>();
    public DbSet<RecommendationImpression> RecommendationImpressions => Set<RecommendationImpression>();
    public DbSet<RecommendationRun> RecommendationRuns => Set<RecommendationRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(512);
        base.ConfigureConventions(configurationBuilder);
    }
}
