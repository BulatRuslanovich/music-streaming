// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Сводки для главной и для библиотеки: свежее, любимое, плейлисты и счётчики. Отдельно от
/// каталога, потому что это агрегаты поверх него, а не чтение сущностей.
/// </summary>
public class LibraryOverviewService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMemoryCache memoryCache,
    CatalogService catalog)
{
    public async Task<HomeSummaryDto> GetHomeSummaryAsync(int sectionSize = 12, CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var projectTrack = ToDto.Track(userId);

        var recentlyAdded = await db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(sectionSize)
            .Select(projectTrack)
            .ToListAsync(ct);

        var lastPlays = db.ListeningHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .GroupBy(h => h.TrackId)
            .Select(g => new { TrackId = g.Key, PlayedAt = g.Max(h => h.PlayedAt) });

        var recentlyPlayed = await (
                from play in lastPlays
                join track in db.Tracks.AsNoTracking() on play.TrackId equals track.Id
                orderby play.PlayedAt descending
                select track)
            .Take(sectionSize)
            .Select(projectTrack)
            .ToListAsync(ct);

        var favorites = await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(sectionSize)
            .Select(f => f.Track!)
            .Select(projectTrack)
            .ToListAsync(ct);

        var albums = await db.Albums.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Album)
            .ToListAsync(ct);

        var playlists = await db.Playlists.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(sectionSize)
            .Select(ToDto.Playlist)
            .ToListAsync(ct);

        return new HomeSummaryDto(
            recentlyAdded, recentlyPlayed, favorites, albums, playlists, await LibraryStatsAsync(userId, ct));
    }

    private const int TopGenreCount = 8;

    public async Task<LibraryOverviewDto> GetLibraryOverviewAsync(int sectionSize, CancellationToken ct)
    {
        var userId = currentUser.Id;

        var recentTracks = await db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Track(userId))
            .ToListAsync(ct);

        var recentAlbums = await db.Albums.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Album)
            .ToListAsync(ct);

        var recentArtists = await db.Artists.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Artist)
            .ToListAsync(ct);

        var topGenres = (await catalog.GetGenresAsync(ct))
            .OrderByDescending(g => g.TrackCount)
            .ThenBy(g => g.Name)
            .Take(TopGenreCount)
            .ToList();

        return new LibraryOverviewDto(
            await LibraryStatsAsync(userId, ct), recentTracks, recentAlbums, recentArtists, topGenres);
    }

    private Task<LibraryStatsDto> LibraryStatsAsync(Guid userId, CancellationToken ct) =>
        memoryCache.GetOrCreateAsync(
            $"library-stats:{userId}",
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return QueryLibraryStatsAsync(userId, ct);
            })!;

    private async Task<LibraryStatsDto> QueryLibraryStatsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Set<LibraryStatsRow>().FromSql(
            $"""
            SELECT (SELECT COUNT(*) FROM tracks)::int                             AS tracks,
                   (SELECT COUNT(*) FROM albums)::int                             AS albums,
                   (SELECT COUNT(*) FROM artists)::int                            AS artists,
                   (SELECT COUNT(*) FROM playlists WHERE user_id = {userId})::int AS playlists,
                   (SELECT COALESCE(SUM(duration_seconds), 0) FROM tracks)::bigint AS duration_seconds,
                   (SELECT COALESCE(SUM(file_size), 0) FROM tracks)::bigint        AS total_bytes,
                   (SELECT COUNT(*) FROM genres)::int                             AS genres,
                   (SELECT COUNT(*) FROM favorites WHERE user_id = {userId})::int AS favorites
            """).ToListAsync(ct);

        var row = rows[0];

        return new LibraryStatsDto(
            row.Tracks, row.Albums, row.Artists, row.Playlists, row.DurationSeconds, row.TotalBytes,
            row.Genres, row.Favorites);
    }
}

// Keyless-проекция: её полное имя записано в снапшот модели EF, поэтому namespace менять нельзя.
public class LibraryStatsRow
{
    public int Tracks { get; set; }
    public int Albums { get; set; }
    public int Artists { get; set; }
    public int Playlists { get; set; }
    public long DurationSeconds { get; set; }
    public long TotalBytes { get; set; }
    public int Genres { get; set; }
    public int Favorites { get; set; }
}
