// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

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

public static class LibrarySeeder
{
    /// <summary>Очередь показов работающего хоста; ставит её фикстура.</summary>
    public static ImpressionQueue? Impressions { get; set; }

    /// <summary>
    /// Ждёт, пока воркер разберёт очередь показов.
    /// </summary>
    /// <remarks>
    /// Показы пишутся из фона, а очистка сносит библиотеку целиком. Вставка в
    /// recommendation_impressions и удаление tracks идут навстречу друг другу по одним и тем же
    /// строкам — Postgres разбивает такую пару дедлоком, и падает тест, который её не звал.
    /// </remarks>
    public static async Task DrainImpressionsAsync()
    {
        if (Impressions is not { } queue)
            return;

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (queue.Handled < queue.Accepted)
        {
            Assert.True(
                DateTimeOffset.UtcNow < deadline,
                "The impression worker did not drain the queue in time.");

            await Task.Delay(25);
        }
    }

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
            var createdAt = DateTimeOffset.UtcNow.AddDays(-a);

            var artist = new Artist
            {
                Name = $"Artist {a}",
                NormalizedName = Normalize.Key($"Artist {a}"),
                CreatedAt = createdAt,
            };
            artists.Add(artist);

            var album = new Album
            {
                Title = $"Album {a}",
                NormalizedTitle = Normalize.Key($"Album {a}"),
                ArtistId = artist.Id,
                Year = 2000 + a,
                CreatedAt = createdAt,
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

    public static async Task ClearAsync(ApplicationDbContext db)
    {
        await DrainImpressionsAsync();

        await db.RecommendationImpressions.ExecuteDeleteAsync();
        await db.RecommendationCache.ExecuteDeleteAsync();
        await db.RecommendationRuns.ExecuteDeleteAsync();
        await db.RecommendationSuppressions.ExecuteDeleteAsync();
        await db.ArtistTags.ExecuteDeleteAsync();
        await db.TrackTags.ExecuteDeleteAsync();
        await db.TrackSimilarities.ExecuteDeleteAsync();
        await db.TrackStats.ExecuteDeleteAsync();
        await db.UserTrackAffinities.ExecuteDeleteAsync();
        await db.UserArtistAffinities.ExecuteDeleteAsync();
        await db.UserGenreAffinities.ExecuteDeleteAsync();
        await db.UserTasteProfiles.ExecuteDeleteAsync();
        await db.PlaybackEvents.ExecuteDeleteAsync();
        await db.DailyMixes.ExecuteDeleteAsync();

        await db.OutboundJobs.ExecuteDeleteAsync();
        await db.LastfmAccounts.ExecuteDeleteAsync();
        await db.UserSettings.ExecuteDeleteAsync();
        await db.ListeningStats.ExecuteDeleteAsync();
        await db.TrackLyrics.ExecuteDeleteAsync();

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
