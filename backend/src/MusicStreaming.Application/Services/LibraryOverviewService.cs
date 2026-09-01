// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Сводки для главной и для библиотеки: свежее, любимое, плейлисты и счётчики. Отдельно от
/// каталога, потому что это агрегаты поверх него, а не чтение сущностей.
/// </summary>
public class LibraryOverviewService(
    IApplicationDbContext db,
    IApplicationDbContextFactory contextFactory,
    ICurrentUser currentUser,
    IMemoryCache memoryCache,
    CatalogService catalog)
{
    public async Task<HomeSummaryDto> GetHomeSummaryAsync(int sectionSize = 12, CancellationToken ct = default)
    {
        var userId = currentUser.Id;

        // Шесть независимых выборок раньше шли одна за другой. Ничто из них не зависит от
        // остальных, а страница теперь рендерится на сервере — то есть их суммарное время
        // стоит перед выдачей HTML, а не после неё.
        var recentlyAdded = contextFactory.QueryAsync(db => db.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Track(userId))
            .ToListAsync(ct));

        var recentlyPlayed = contextFactory.QueryAsync(db => RecentlyPlayedAsync(db, userId, sectionSize, ct));

        var favorites = contextFactory.QueryAsync(db => db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(sectionSize)
            .Select(f => f.Track!)
            .Select(ToDto.Track(userId))
            .ToListAsync(ct));

        var albums = contextFactory.QueryAsync(db => db.Albums.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Album)
            .ToListAsync(ct));

        var playlists = contextFactory.QueryAsync(db => db.Playlists.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(sectionSize)
            .Select(ToDto.Playlist)
            .ToListAsync(ct));

        var stats = LibraryStatsAsync(userId, ct);

        await Task.WhenAll(recentlyAdded, recentlyPlayed, favorites, albums, playlists, stats);

        return new HomeSummaryDto(
            await recentlyAdded, await recentlyPlayed, await favorites,
            await albums, await playlists, await stats);
    }

    /// <summary>
    /// Последние прослушанные треки, без повторов.
    /// </summary>
    /// <remarks>
    /// Раньше здесь был GROUP BY по всей истории пользователя с MAX(played_at): постгресу
    /// приходилось прочитать и свернуть всю его партицию, чтобы отдать двенадцать строк.
    /// Нам же нужны последние N различных треков, а не сводка за всё время, поэтому берём
    /// окно свежих прослушиваний по индексу (user_id, played_at) и схлопываем повторы уже в нём.
    /// Окно с запасом: даже если человек гонял один трек по кругу, двенадцать разных наберётся.
    /// </remarks>
    private static async Task<List<TrackDto>> RecentlyPlayedAsync(
        IApplicationDbContext db, Guid userId, int sectionSize, CancellationToken ct)
    {
        var window = Math.Max(RecentPlayWindow, sectionSize * 20);

        var recent = await db.ListeningHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.PlayedAt)
            .Take(window)
            .Select(h => h.TrackId)
            .ToListAsync(ct);

        var ordered = recent.Distinct().Take(sectionSize).ToList();
        if (ordered.Count == 0)
            return [];

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => ordered.Contains(t.Id))
            .Select(ToDto.Track(userId))
            .ToListAsync(ct);

        var byId = tracks.ToDictionary(track => track.Id);

        // Порядок задаёт история, а не то, в каком порядке база вернула строки.
        return [.. ordered.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    private const int RecentPlayWindow = 200;

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
            RecommendationCacheKeys.LibraryStats(userId),
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
                   (SELECT COALESCE(SUM(duration_seconds), 0) FROM tracks)::bigint AS duration_seconds,
                   (SELECT COALESCE(SUM(file_size), 0) FROM tracks)::bigint        AS total_bytes,
                   (SELECT COUNT(*) FROM favorites WHERE user_id = {userId})::int AS favorites
            """).ToListAsync(ct);

        var row = rows[0];

        return new LibraryStatsDto(
            row.Tracks, row.Albums, row.DurationSeconds, row.TotalBytes, row.Favorites);
    }
}

// Keyless-проекция: её полное имя записано в снапшот модели EF, поэтому namespace менять нельзя.
public class LibraryStatsRow
{
    public int Tracks { get; set; }
    public int Albums { get; set; }
    public long DurationSeconds { get; set; }
    public long TotalBytes { get; set; }
    public int Favorites { get; set; }
}
