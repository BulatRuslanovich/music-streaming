// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Admin;

/// <summary>
/// Что в каталоге просит внимания: пробелы в метаданных и треки, которые слушатели отвергают.
/// </summary>
/// <remarks>
/// Период сюда намеренно не передаётся. Отсутствие обложки — это состояние трека, а не событие
/// внутри выбранных семи дней, и «за неделю у нас 12 треков без жанра» не значит ничего.
/// </remarks>
public class AdminCatalogHealthService(IApplicationDbContext db)
{
    /// <summary>Доля пропусков, с которой трек считается проблемным.</summary>
    private const double HighSkipRate = 0.6;

    /// <summary>
    /// Меньше стольких завершений и пропусков — выборка не показательна, и один случайный
    /// пропуск не должен помечать композицию как проблемную.
    /// </summary>
    private const int SkipRateMinimumEvents = 10;

    public async Task<AdminCatalogHealthDto> GetAsync(CancellationToken ct)
    {
        // Все пробелы в метаданных — одной группировкой: это семь FILTER-ов над одним проходом,
        // а не семь отдельных COUNT по таблице треков.
        var gaps = await db.Tracks.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WithoutCover = g.Count(t => t.Album == null || t.Album.CoverPath == null),
                WithoutLyrics = g.Count(t => t.Lyrics == null),
                WithoutGenre = g.Count(t => t.GenreId == null),
                WithoutAlbum = g.Count(t => t.AlbumId == null),
                WithoutYear = g.Count(t => t.Year == null),
            })
            .FirstOrDefaultAsync(ct);

        var listened = await db.ListeningStats.AsNoTracking()
            .Select(s => s.TrackId)
            .Distinct()
            .CountAsync(ct);

        var total = gaps?.Total ?? 0;

        return new AdminCatalogHealthDto(
            total,
            gaps?.WithoutCover ?? 0,
            gaps?.WithoutLyrics ?? 0,
            gaps?.WithoutGenre ?? 0,
            gaps?.WithoutAlbum ?? 0,
            gaps?.WithoutYear ?? 0,
            Math.Max(0, total - listened),
            await HighSkipRateAsync(ct),
            HighSkipRate,
            SkipRateMinimumEvents);
    }

    private Task<int> HighSkipRateAsync(CancellationToken ct) =>
        db.PlaybackEvents.AsNoTracking()
            .Where(e => e.TrackId != null
                        && (e.Type == PlaybackEventType.TrackCompleted
                            || e.Type == PlaybackEventType.TrackSkipped))
            .GroupBy(e => e.TrackId!.Value)
            .Select(g => new
            {
                Finished = g.Count(),
                Skipped = g.Count(e => e.Type == PlaybackEventType.TrackSkipped),
            })
            .Where(t => t.Finished >= SkipRateMinimumEvents
                        && t.Skipped > t.Finished * HighSkipRate)
            .CountAsync(ct);
}
