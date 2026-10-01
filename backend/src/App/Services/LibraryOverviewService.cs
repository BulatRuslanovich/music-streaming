// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace App.Services;

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

        var recentlyAdded = contextFactory.QueryAsync(d => d.Tracks.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Track(userId))
            .ToListAsync(ct));

        var recentlyPlayed = contextFactory.QueryAsync(async d =>
        {
            var recent = await d.ListeningHistory.AsNoTracking()
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.PlayedAt)
                .Take(Math.Max(RecentPlayWindow, sectionSize * 20))
                .Select(h => h.TrackId)
                .ToListAsync(ct);

            var ordered = recent.Distinct().Take(sectionSize).ToList();
            if (ordered.Count == 0)
                return [];

            var byId = await d.Tracks.AsNoTracking()
                .Where(t => ordered.Contains(t.Id))
                .Select(ToDto.Track(userId))
                .ToDictionaryAsync(track => track.Id, ct);

            return ordered.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        });

        var favorites = contextFactory.QueryAsync(d => d.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(sectionSize)
            .Select(f => f.Track!)
            .Select(ToDto.Track(userId))
            .ToListAsync(ct));

        var albums = contextFactory.QueryAsync(d => d.Albums.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(sectionSize)
            .Select(ToDto.Album)
            .ToListAsync(ct));

        var playlists = contextFactory.QueryAsync(d => d.Playlists.AsNoTracking()
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
            $"library-stats:{userId}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);

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
            })!;
}

public class LibraryStatsRow
{
    public int Tracks { get; set; }
    public int Albums { get; set; }
    public long DurationSeconds { get; set; }
    public long TotalBytes { get; set; }
    public int Favorites { get; set; }
}
