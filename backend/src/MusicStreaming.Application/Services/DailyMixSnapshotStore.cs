// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Плейлист дня фиксируется на локальную дату слушателя: первый заход за день собирает микс и
/// запоминает его, все остальные читают тот же снимок. Пул под ним живёт своей жизнью —
/// рекомендации пересчитываются после каждой сессии, а витрины ещё и меняются по времени
/// суток, — так что без снимка «подборка на сегодня» переписывалась бы по нескольку раз в день.
/// </summary>
public class DailyMixSnapshotStore(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    LibraryOverviewService overview,
    RecommendationService recommendations,
    UserSettingsService settings,
    TimeProvider clock)
{
    /// <summary>Размер плейлиста дня: он собирается раз в сутки, поэтому его хватает на весь день.</summary>
    private const int DailyMixSize = 60;

    /// <summary>Вес трека, попавшего в пул без скора (избранное и свежие поступления на подхвате).</summary>
    private const double FallbackWeight = 0.15;

    public async Task<IReadOnlyList<TrackDto>> TodayAsync(CancellationToken ct)
    {
        var userId = currentUser.Id;
        var localDate = await LocalDateAsync(ct);

        var stored = await db.DailyMixes.AsNoTracking()
            .FirstOrDefaultAsync(mix => mix.UserId == userId && mix.LocalDate == localDate, ct);

        var trackIds = stored?.TrackIds ?? await BuildAsync(userId, localDate, ct);
        if (trackIds.Count == 0)
            return [];

        // Треки могли удалить уже после того, как микс был собран.
        var known = await db.TracksByIdAsync(userId, trackIds, ct);

        return [.. trackIds.Where(known.ContainsKey).Select(id => known[id])];
    }

    private async Task<IReadOnlyList<Guid>> BuildAsync(
        Guid userId, DateOnly localDate, CancellationToken ct)
    {
        // Скоры нужны только для взвешивания микса и наружу не отдаются.
        var personal = await recommendations.GetHomeAsync(DailyMixSize, includeScores: true, ct: ct);

        var seen = new HashSet<Guid>();
        var pool = new List<(Guid Id, double Weight)>();

        foreach (var section in personal.Sections)
        {
            if (section.BaseKey is ShelfKeys.Popular or ShelfKeys.NewReleases or ShelfKeys.ContinueListening)
                continue;

            foreach (var item in section.Tracks ?? [])
                if (seen.Add(item.Track.Id))
                    pool.Add((item.Track.Id, item.Score ?? FallbackWeight));
        }

        if (pool.Count < DailyMixSize)
        {
            var summary = await overview.GetHomeSummaryAsync(DailyMixSize, ct);

            foreach (var track in summary.Favorites.Concat(summary.RecentlyAdded))
                if (seen.Add(track.Id))
                    pool.Add((track.Id, FallbackWeight));
        }

        if (pool.Count < HomeBlocks.MinimumHeroSize)
            return [];

        var picked = DailyMix.PickWeighted(userId, localDate, pool, DailyMixSize);

        return await StoreAsync(userId, localDate, picked, ct);
    }

    private async Task<IReadOnlyList<Guid>> StoreAsync(
        Guid userId, DateOnly localDate, IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        db.DailyMixes.Add(new DailyMixSnapshot
        {
            UserId = userId,
            LocalDate = localDate,
            TrackIds = trackIds,
            GeneratedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Параллельный запрос успел записать снимок на этот день — он и остаётся сегодняшним.
            db.ChangeTracker.Clear();

            var stored = await db.DailyMixes.AsNoTracking()
                .FirstOrDefaultAsync(mix => mix.UserId == userId && mix.LocalDate == localDate, ct);

            return stored?.TrackIds ?? trackIds;
        }

        await db.DailyMixes
            .Where(mix => mix.UserId == userId && mix.LocalDate < localDate)
            .ExecuteDeleteAsync(ct);

        return trackIds;
    }

    private async Task<DateOnly> LocalDateAsync(CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);

        return DateOnly.FromDateTime(local.DateTime);
    }
}
