// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.IntegrationTests.Evaluation;

public record EvaluationScene(
    string Name,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyList<Guid> ArtistIds,
    IReadOnlyList<Guid> TrackIds);

/// <summary>
/// Библиотека со «сценами»: группы исполнителей, у каждой свои теги и своя эпоха. Вкус слушателя —
/// это сцена, и качество ранжирования измеримо: попадает ли рекомендация в ту сцену, из которой
/// человек потом действительно слушал.
///
/// Сцена намеренно охватывает несколько жанров. Если приравнять сцену к жанру, измерение упрётся
/// в <see cref="MusicStreaming.Application.Options.RecommendationOptions.MaxPerGenre"/> — квота
/// разнообразия срежет вкус до трети полки, и мерить мы будем её, а не ранжирование.
/// </summary>
public record EvaluationCatalog(IReadOnlyList<EvaluationScene> Scenes)
{
    private readonly Dictionary<Guid, EvaluationScene> _byTrack = Scenes
        .SelectMany(scene => scene.TrackIds.Select(trackId => (trackId, scene)))
        .ToDictionary(pair => pair.trackId, pair => pair.scene);

    public int TrackCount => Scenes.Sum(scene => scene.TrackIds.Count);

    public EvaluationScene? SceneOf(Guid trackId) =>
        _byTrack.TryGetValue(trackId, out var scene) ? scene : null;
}

public static class EvaluationLibrary
{
    private static readonly string[][] SceneTags =
    [
        ["shoegaze", "dream pop", "noise pop", "reverb"],
        ["techno", "minimal", "club", "four on the floor"],
        ["folk", "acoustic", "singer-songwriter", "quiet"],
    ];

    public static async Task<EvaluationCatalog> SeedAsync(
        ApplicationDbContext db,
        int sceneCount = 3,
        int artistsPerScene = 4,
        int tracksPerArtist = 8,
        int genresPerScene = 3)
    {
        await LibrarySeeder.ClearAsync(db);

        var scenes = new List<EvaluationScene>(sceneCount);
        var genres = new List<Genre>();
        var artists = new List<Artist>();
        var albums = new List<Album>();
        var tracks = new List<Track>();
        var artistTags = new List<ArtistTag>();

        for (var s = 0; s < sceneCount; s++)
        {
            var name = $"Scene {s}";

            var sceneGenres = Enumerable.Range(0, genresPerScene)
                .Select(index =>
                {
                    var genreName = $"{name} Genre {index}";
                    return new Genre { Name = genreName, NormalizedName = Normalize.Key(genreName) };
                })
                .ToList();

            genres.AddRange(sceneGenres);

            var sceneArtists = new List<Guid>(artistsPerScene);
            var sceneTracks = new List<Guid>(artistsPerScene * tracksPerArtist);

            for (var a = 0; a < artistsPerScene; a++)
            {
                var genre = sceneGenres[a % sceneGenres.Count];
                var artistName = $"{name} Artist {a}";
                var artist = new Artist { Name = artistName, NormalizedName = Normalize.Key(artistName) };
                artists.Add(artist);
                sceneArtists.Add(artist.Id);

                var tags = SceneTags[s % SceneTags.Length];
                for (var t = 0; t < tags.Length; t++)
                {
                    artistTags.Add(new ArtistTag
                    {
                        ArtistId = artist.Id,
                        Name = tags[t],
                        Weight = 1.0 - 0.15 * t,
                    });
                }

                var albumTitle = $"{artistName} Album";
                var album = new Album
                {
                    Title = albumTitle,
                    NormalizedTitle = Normalize.Key(albumTitle),
                    ArtistId = artist.Id,
                    Year = 1990 + s * 10 + a,
                };

                albums.Add(album);

                for (var t = 0; t < tracksPerArtist; t++)
                {
                    var title = $"{artistName} Track {t}";
                    var track = new Track
                    {
                        Title = title,
                        NormalizedTitle = Normalize.Key(title),
                        ArtistId = artist.Id,
                        AlbumId = album.Id,
                        GenreId = genre.Id,
                        Year = album.Year,
                        TrackNumber = t + 1,
                        DurationSeconds = 170 + (t * 7 % 90),
                        FilePath = $"music/eval-{s}-{a}-{t}.mp3",
                        OriginalFileName = $"eval-{s}-{a}-{t}.mp3",
                        ContentHash = $"eval-{s:D2}{a:D2}{t:D2}",
                        FileSize = 4_000_000,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-180 + s * 7 + a),
                    };

                    tracks.Add(track);
                    sceneTracks.Add(track.Id);
                }
            }

            scenes.Add(new EvaluationScene(
                name, [.. sceneGenres.Select(item => item.Id)], sceneArtists, sceneTracks));
        }

        db.Genres.AddRange(genres);
        db.Artists.AddRange(artists);
        db.Albums.AddRange(albums);
        db.Tracks.AddRange(tracks);
        await db.SaveChangesAsync();

        db.TrackArtists.AddRange(tracks.Select(track => new TrackArtist
        {
            TrackId = track.Id,
            ArtistId = track.ArtistId,
            Position = 0,
        }));

        db.ArtistTags.AddRange(artistTags);
        await db.SaveChangesAsync();

        return new EvaluationCatalog(scenes);
    }
}
