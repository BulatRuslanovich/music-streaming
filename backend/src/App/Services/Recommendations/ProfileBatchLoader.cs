// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Recommendations;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Domain.Entities.Recommendations;

namespace App.Services.Recommendations;

public record TrackMetadata(Guid? GenreId, IReadOnlyList<Guid> ArtistIds);

public record ProfileBatchData(
    Dictionary<Guid, TrackMetadata> Metadata,
    Dictionary<Guid, Guid> AlbumArtists,
    Dictionary<Guid, UserTrackAffinity> Tracks,
    Dictionary<Guid, UserArtistAffinity> Artists,
    Dictionary<Guid, UserGenreAffinity> Genres,
    Dictionary<(Guid TrackId, DateTimeOffset Hour), ListeningStat> Listening,
    HashSet<Guid> ExistingArtists);

public class ProfileBatchLoader(IApplicationDbContext db)
{
    public async Task<ProfileBatchData> LoadAsync(
        Guid userId, IReadOnlyList<PlaybackEvent> batch, CancellationToken ct)
    {
        List<Guid> trackIds = [.. batch.Where(e => e.TrackId is not null).Select(e => e.TrackId!.Value).Distinct()];
        var albumIds = Opened(PlaybackEventType.AlbumOpened);
        var opened = Opened(PlaybackEventType.ArtistOpened);

        Dictionary<Guid, TrackMetadata> metadata = trackIds.Count == 0
            ? []
            : (await db.Tracks.AsNoTracking()
                .Where(t => trackIds.Contains(t.Id))
                .Select(t => new { t.Id, t.GenreId, t.ArtistId, Credits = t.TrackArtists.Select(ta => ta.ArtistId).ToList() })
                .ToListAsync(ct))
            .ToDictionary(
                row => row.Id,
                row => new TrackMetadata(
                    row.GenreId,
                    row.Credits.Contains(row.ArtistId) ? row.Credits : [.. row.Credits, row.ArtistId]));

        Dictionary<Guid, Guid> albumArtists = albumIds.Count == 0
            ? []
            : await db.Albums.AsNoTracking()
                .Where(a => albumIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.ArtistId, ct);

        Dictionary<Guid, UserTrackAffinity> tracks = trackIds.Count == 0
            ? []
            : await db.UserTrackAffinities
                .Where(a => a.UserId == userId && trackIds.Contains(a.TrackId))
                .ToDictionaryAsync(a => a.TrackId, ct);

        List<Guid> artistIds =
            [.. metadata.Values.SelectMany(track => track.ArtistIds).Concat(albumArtists.Values).Concat(opened).Distinct()];

        Dictionary<Guid, UserArtistAffinity> artists = artistIds.Count == 0
            ? []
            : await db.UserArtistAffinities
                .Where(a => a.UserId == userId && artistIds.Contains(a.ArtistId))
                .ToDictionaryAsync(a => a.ArtistId, ct);

        List<Guid> genreIds = [.. metadata.Values.Where(t => t.GenreId is not null).Select(t => t.GenreId!.Value).Distinct()];

        Dictionary<Guid, UserGenreAffinity> genres = genreIds.Count == 0
            ? []
            : await db.UserGenreAffinities
                .Where(a => a.UserId == userId && genreIds.Contains(a.GenreId))
                .ToDictionaryAsync(a => a.GenreId, ct);

        var attempts = batch.Select(PlayAttempt.From).OfType<PlayAttempt>().ToList();
        var listening = new Dictionary<(Guid TrackId, DateTimeOffset Hour), ListeningStat>();

        if (attempts.Count > 0)
        {
            var from = attempts.Min(a => a.Hour);
            var to = attempts.Max(a => a.Hour);
            var hourTrackIds = attempts.Select(a => a.TrackId).Distinct().ToList();

            listening = (await db.ListeningStats
                    .Where(s => s.UserId == userId && s.Hour >= from && s.Hour <= to && hourTrackIds.Contains(s.TrackId))
                    .ToListAsync(ct))
                .ToDictionary(row => (row.TrackId, row.Hour));
        }

        HashSet<Guid> existingArtists = opened.Count == 0
            ? []
            : [.. await db.Artists.AsNoTracking().Where(a => opened.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct)];

        return new ProfileBatchData(metadata, albumArtists, tracks, artists, genres, listening, existingArtists);

        List<Guid> Opened(PlaybackEventType type) =>
            [.. batch.Where(e => e.Type == type && e.EntityId is not null).Select(e => e.EntityId!.Value).Distinct()];
    }
}
