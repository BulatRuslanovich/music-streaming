// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using System.Buffers.Binary;
using App.Abstractions;
using App.Dtos;
using App.Services;
using Microsoft.EntityFrameworkCore;
using App.Common;
using Domain.Entities.Recommendations;

namespace App.Recommendations.Home;

public class DailyMixSnapshotStore(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    LibraryOverviewService overview,
    RecommendationService recommendations,
    UserSettingsService settings,
    TimeProvider clock)
{
    private const int DailyMixSize = 60;

    private const double FallbackWeight = 0.15;

    private const ulong FnvOffset = 14695981039346656037;

    private const ulong FnvPrime = 1099511628211;

    private const double MinimumWeight = 0.02;

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

            // Взвешенная выборка без возвращения (Efraimidis–Spirakis) с ключами из хэша пользователя,
            // даты и трека: микс стабилен в течение дня и меняется на следующий.
            var bytes = new byte[16];
            userId.TryWriteBytes(bytes, bigEndian: true, out _);
            var seed = Hash(FnvOffset, bytes);
            BinaryPrimitives.WriteInt32BigEndian(bytes, localDate.DayNumber);
            seed = Hash(seed, bytes.AsSpan(0, 4));

            var keyed = new List<(Guid Id, double Key)>(pool.Count);

            foreach (var (id, weight) in pool)
            {
                id.TryWriteBytes(bytes, bigEndian: true, out _);
                var hash = Hash(seed, bytes);

                hash ^= hash >> 33;
                hash *= 0xff51afd7ed558ccd;
                hash ^= hash >> 33;
                hash *= 0xc4ceb9fe1a85ec53;
                hash ^= hash >> 33;

                var u = (hash + 1.0) / (ulong.MaxValue + 1.0);
                keyed.Add((id, Math.Log(Math.Clamp(u, double.Epsilon, 1)) / (Math.Max(weight, 0) + MinimumWeight)));
            }

            trackIds =
            [
                .. keyed
                    .OrderByDescending(item => item.Key)
                    .ThenBy(item => item.Id)
                    .Take(DailyMixSize)
                    .Select(item => item.Id)
            ];

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

    private static ulong Hash(ulong start, ReadOnlySpan<byte> data)
    {
        var hash = start;

        foreach (var value in data)
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        return hash;
    }
}
