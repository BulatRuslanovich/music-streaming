using Microsoft.EntityFrameworkCore;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.IntegrationTests;

/// <summary>What a seeded library looks like, so tests can refer to its parts by name.</summary>
public record SeededLibrary(
    Guid UserId,
    IReadOnlyList<Guid> ArtistIds,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyList<Guid> AlbumIds,
    IReadOnlyList<Guid> TrackIds)
{
    public Guid Track(int index) => TrackIds[index];
    public Guid Artist(int index) => ArtistIds[index];
}

/// <summary>
/// Builds a small but structurally complete library: several artists, albums with real track
/// groupings, two genres, and a collaboration — enough for content similarity to have something
/// to say.
/// </summary>
public static class LibrarySeeder
{
    public static async Task<SeededLibrary> SeedAsync(
        ApplicationDbContext db, int artistCount = 4, int tracksPerArtist = 5)
    {
        await ClearAsync(db);

        var user = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).FirstAsync();

        var genres = new List<Genre>
        {
            NewGenre("Integration Rock"),
            NewGenre("Integration Electronic"),
        };

        db.Genres.AddRange(genres);

        var artists = new List<Artist>();
        var albums = new List<Album>();
        var tracks = new List<Track>();

        for (var a = 0; a < artistCount; a++)
        {
            var artist = new Artist { Name = $"Artist {a}", NormalizedName = Normalize.Key($"Artist {a}") };
            artists.Add(artist);

            var album = new Album
            {
                Title = $"Album {a}",
                NormalizedTitle = Normalize.Key($"Album {a}"),
                ArtistId = artist.Id,
                Year = 2000 + a,
            };

            albums.Add(album);

            for (var t = 0; t < tracksPerArtist; t++)
            {
                var index = a * tracksPerArtist + t;

                tracks.Add(new Track
                {
                    Title = $"Track {index}",
                    NormalizedTitle = Normalize.Key($"Track {index}"),
                    ArtistId = artist.Id,
                    AlbumId = album.Id,
                    GenreId = genres[a % genres.Count].Id,
                    Year = 2000 + a,
                    TrackNumber = t + 1,
                    DurationSeconds = 180 + index,
                    FilePath = $"music/integration-{index}.mp3",
                    OriginalFileName = $"integration-{index}.mp3",
                    ContentHash = $"hash-{index:D8}",
                    FileSize = 4_000_000,
                    // Staggered so "recently added" has a meaningful order.
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-index),
                });
            }
        }

        db.Artists.AddRange(artists);
        db.Albums.AddRange(albums);
        db.Tracks.AddRange(tracks);
        await db.SaveChangesAsync();

        db.TrackArtists.AddRange(tracks.Select(t => new TrackArtist
        {
            TrackId = t.Id,
            ArtistId = t.ArtistId,
            Position = 0,
        }));

        // One collaboration, so that credit-based similarity is exercised rather than assumed.
        if (artistCount > 1)
        {
            db.TrackArtists.Add(new TrackArtist
            {
                TrackId = tracks[0].Id,
                ArtistId = artists[1].Id,
                Position = 1,
            });
        }

        await db.SaveChangesAsync();

        return new SeededLibrary(
            user.Id,
            artists.Select(a => a.Id).ToList(),
            genres.Select(g => g.Id).ToList(),
            albums.Select(a => a.Id).ToList(),
            tracks.Select(t => t.Id).ToList());
    }

    /// <summary>
    /// Resets everything a test could have written. Ordered so that no foreign key is left
    /// dangling, and deliberately explicit rather than a TRUNCATE CASCADE — a test that leaves a
    /// new table behind should fail here rather than silently share state with the next one.
    /// </summary>
    public static async Task ClearAsync(ApplicationDbContext db)
    {
        await db.RecommendationImpressions.ExecuteDeleteAsync();
        await db.RecommendationCache.ExecuteDeleteAsync();
        await db.RecommendationRuns.ExecuteDeleteAsync();
        await db.TrackSimilarities.ExecuteDeleteAsync();
        await db.TrackStats.ExecuteDeleteAsync();
        await db.UserTrackAffinities.ExecuteDeleteAsync();
        await db.UserArtistAffinities.ExecuteDeleteAsync();
        await db.UserGenreAffinities.ExecuteDeleteAsync();
        await db.UserTasteProfiles.ExecuteDeleteAsync();
        await db.PlaybackEvents.ExecuteDeleteAsync();

        await db.ListeningHistory.ExecuteDeleteAsync();
        await db.Favorites.ExecuteDeleteAsync();
        await db.PlaylistTracks.ExecuteDeleteAsync();
        await db.Playlists.ExecuteDeleteAsync();
        await db.TrackArtists.ExecuteDeleteAsync();
        await db.Tracks.ExecuteDeleteAsync();
        await db.Albums.ExecuteDeleteAsync();
        await db.Artists.ExecuteDeleteAsync();
        await db.Genres.ExecuteDeleteAsync();
    }

    private static Genre NewGenre(string name) =>
        new() { Name = name, NormalizedName = Normalize.Key(name) };
}
