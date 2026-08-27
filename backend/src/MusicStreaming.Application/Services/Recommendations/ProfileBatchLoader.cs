// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public record TrackMetadata(Guid? GenreId, IReadOnlyList<Guid> ArtistIds);

/// <summary>
/// Всё, что нужно пачке событий, одним куском. Словари привязанностей загружаются отслеживаемыми
/// намеренно: свёртка их правит и сохраняет, а новые записи досоздаёт по ходу.
/// </summary>
public record ProfileBatchData(
    Dictionary<Guid, TrackMetadata> Metadata,
    Dictionary<Guid, Guid> AlbumArtists,
    Dictionary<Guid, UserTrackAffinity> Tracks,
    Dictionary<Guid, UserArtistAffinity> Artists,
    Dictionary<Guid, UserGenreAffinity> Genres,
    Dictionary<(Guid TrackId, DateTimeOffset Hour), ListeningStat> Listening,
    HashSet<Guid> ExistingArtists);

/// <summary>
/// Читает из базы всё, что понадобится <see cref="ProfileRollupService"/> для одной пачки
/// событий — за фиксированное число запросов, а не по запросу на событие.
/// </summary>
public class ProfileBatchLoader(IApplicationDbContext db)
{
    public async Task<ProfileBatchData> LoadAsync(
        Guid userId, IReadOnlyList<PlaybackEvent> batch, CancellationToken ct)
    {
        var trackIds = batch.Where(e => e.TrackId is not null).Select(e => e.TrackId!.Value).Distinct().ToList();

        var metadata = await LoadTrackMetadataAsync(trackIds, ct);
        var albumArtists = await LoadAlbumArtistsAsync(batch, ct);
        var tracks = await LoadAffinitiesAsync(userId, trackIds, ct);

        var opened = OpenedArtistsOf(batch);

        var artists = await LoadArtistAffinitiesAsync(userId, ArtistsOf(metadata, albumArtists, opened), ct);
        var genres = await LoadGenreAffinitiesAsync(userId, GenresOf(metadata), ct);
        var listening = await LoadListeningHoursAsync(userId, HoursOf(batch), ct);
        var existingArtists = await LoadExistingArtistsAsync(opened, ct);

        return new ProfileBatchData(
            metadata, albumArtists, tracks, artists, genres, listening, existingArtists);
    }

    private async Task<Dictionary<Guid, TrackMetadata>> LoadTrackMetadataAsync(
        IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        if (trackIds.Count == 0)
            return [];

        var rows = await db.Tracks.AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.GenreId,
                t.ArtistId,
                Credits = t.TrackArtists.Select(ta => ta.ArtistId).ToList(),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => row.Id,
            row => new TrackMetadata(
                row.GenreId,
                row.Credits.Contains(row.ArtistId) ? row.Credits : [.. row.Credits, row.ArtistId]));
    }

    private async Task<Dictionary<Guid, Guid>> LoadAlbumArtistsAsync(
        IReadOnlyList<PlaybackEvent> batch, CancellationToken ct)
    {
        var albumIds = batch
            .Where(e => e.Type == PlaybackEventType.AlbumOpened && e.EntityId is not null)
            .Select(e => e.EntityId!.Value)
            .Distinct()
            .ToList();

        if (albumIds.Count == 0)
            return [];

        return await db.Albums.AsNoTracking()
            .Where(a => albumIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.ArtistId, ct);
    }

    private async Task<Dictionary<Guid, UserTrackAffinity>> LoadAffinitiesAsync(
        Guid userId, IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        if (trackIds.Count == 0)
            return [];

        return await db.UserTrackAffinities
            .Where(a => a.UserId == userId && trackIds.Contains(a.TrackId))
            .ToDictionaryAsync(a => a.TrackId, ct);
    }

    private async Task<Dictionary<Guid, UserArtistAffinity>> LoadArtistAffinitiesAsync(
        Guid userId, List<Guid> artistIds, CancellationToken ct)
    {
        if (artistIds.Count == 0)
            return [];

        return await db.UserArtistAffinities
            .Where(a => a.UserId == userId && artistIds.Contains(a.ArtistId))
            .ToDictionaryAsync(a => a.ArtistId, ct);
    }

    private async Task<Dictionary<Guid, UserGenreAffinity>> LoadGenreAffinitiesAsync(
        Guid userId, List<Guid> genreIds, CancellationToken ct)
    {
        if (genreIds.Count == 0)
            return [];

        return await db.UserGenreAffinities
            .Where(a => a.UserId == userId && genreIds.Contains(a.GenreId))
            .ToDictionaryAsync(a => a.GenreId, ct);
    }

    private async Task<Dictionary<(Guid TrackId, DateTimeOffset Hour), ListeningStat>> LoadListeningHoursAsync(
        Guid userId, List<(Guid TrackId, DateTimeOffset Hour)> hours, CancellationToken ct)
    {
        if (hours.Count == 0)
            return [];

        var from = hours.Min(h => h.Hour);
        var to = hours.Max(h => h.Hour);
        var trackIds = hours.Select(h => h.TrackId).Distinct().ToList();

        var rows = await db.ListeningStats
            .Where(s => s.UserId == userId
                        && s.Hour >= from
                        && s.Hour <= to
                        && trackIds.Contains(s.TrackId))
            .ToListAsync(ct);

        return rows.ToDictionary(row => (row.TrackId, row.Hour));
    }

    private async Task<HashSet<Guid>> LoadExistingArtistsAsync(List<Guid> artistIds, CancellationToken ct)
    {
        if (artistIds.Count == 0)
            return [];

        var found = await db.Artists.AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(ct);

        return [.. found];
    }

    private static List<Guid> ArtistsOf(
        Dictionary<Guid, TrackMetadata> metadata,
        Dictionary<Guid, Guid> albumArtists,
        List<Guid> opened) =>
        [.. metadata.Values
            .SelectMany(track => track.ArtistIds)
            .Concat(albumArtists.Values)
            .Concat(opened)
            .Distinct()];

    private static List<Guid> OpenedArtistsOf(IReadOnlyList<PlaybackEvent> batch) =>
        [.. batch
            .Where(e => e.Type == PlaybackEventType.ArtistOpened && e.EntityId is not null)
            .Select(e => e.EntityId!.Value)
            .Distinct()];

    private static List<Guid> GenresOf(Dictionary<Guid, TrackMetadata> metadata) =>
        [.. metadata.Values
            .Where(track => track.GenreId is not null)
            .Select(track => track.GenreId!.Value)
            .Distinct()];

    private static List<(Guid TrackId, DateTimeOffset Hour)> HoursOf(IReadOnlyList<PlaybackEvent> batch) =>
        [.. batch
            .Select(PlayAttempt.From)
            .Where(attempt => attempt is not null)
            .Select(attempt => (attempt!.Value.TrackId, attempt.Value.Hour))
            .Distinct()];
}
