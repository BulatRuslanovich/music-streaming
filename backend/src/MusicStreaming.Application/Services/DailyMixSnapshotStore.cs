// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services;

public class DailyMixSnapshotStore(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    LibraryOverviewService overview,
    RecommendationService recommendations,
    UserSettingsService settings,
    TimeProvider clock)
{
    private const int DailyMixSize = 60;

    private const double FallbackWeight = 0.15;

    public async Task<IReadOnlyList<TrackDto>> TodayAsync(CancellationToken ct)
    {
        var userId = currentUser.Id;
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await settings.GetAsync(ct)).TimeZone);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);

        var trackIds = (await db.DailyMixes.AsNoTracking()
            .FirstOrDefaultAsync(mix => mix.UserId == userId && mix.LocalDate == localDate, ct))?.TrackIds;

        if (trackIds is null)
        {
            var seen = new HashSet<Guid>();
            var pool = new List<(Guid Id, double Weight)>();

            foreach (var item in await recommendations.GetMixPoolAsync(ct))
                if (seen.Add(item.Track.Id))
                    pool.Add((item.Track.Id, item.Score ?? FallbackWeight));

            if (pool.Count < DailyMixSize)
            {
                var summary = await overview.GetHomeSummaryAsync(DailyMixSize, ct);

                foreach (var track in summary.Favorites.Concat(summary.RecentlyAdded))
                    if (seen.Add(track.Id))
                        pool.Add((track.Id, FallbackWeight));
            }

            if (pool.Count < HomeFeedService.MinimumHeroSize)
                return [];

            trackIds = DailyMix.PickWeighted(userId, localDate, pool, DailyMixSize);
            db.DailyMixes.Add(new DailyMixSnapshot { UserId = userId, LocalDate = localDate, TrackIds = trackIds });

            try
            {
                await db.SaveChangesAsync(ct);
                await db.DailyMixes
                    .Where(mix => mix.UserId == userId && mix.LocalDate < localDate)
                    .ExecuteDeleteAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();

                trackIds = (await db.DailyMixes.AsNoTracking()
                    .FirstOrDefaultAsync(mix => mix.UserId == userId && mix.LocalDate == localDate, ct))?.TrackIds
                    ?? trackIds;
            }
        }

        if (trackIds.Count == 0)
            return [];

        var known = await db.TracksByIdAsync(userId, trackIds, ct);

        return [.. trackIds.Where(known.ContainsKey).Select(id => known[id])];
    }
}
