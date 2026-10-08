// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;
using App.Services;

namespace App.Recommendations.Moods;

public enum Daypart
{
    Morning,
    Day,
    Evening,
    Night,
}

// Что слушатель слушает в это время суток чаще обычного. По истории считается доля каждого настроения
// в каждой части суток и вычитается его общая доля: вечером грустного на 15 п.п. больше — у грустного
// подъём +0.15. Подъёмы в сумме нулевые, поэтому слушателю, у которого вечер звучит как день, контекст
// ничего не меняет. Подсказка трека — сумма подъёмов, взвешенных его рангами настроений.
public class DaypartMoods(
    ApplicationDbContext db, MoodCatalog moods, UserSettingsService settings, IMemoryCache cache, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(90);

    // Меньше прослушиваний в эту часть суток — доли случайны, контекст не включается.
    public const int MinimumDaypartPlays = 20;

    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    public static Daypart Of(int hour) => hour switch
    {
        >= 5 and < 11 => Daypart.Morning,
        >= 11 and < 17 => Daypart.Day,
        >= 17 and < 23 => Daypart.Evening,
        _ => Daypart.Night,
    };

    // Подсказка для каждого трека библиотеки сейчас; null — контекста нет.
    public async Task<float[]?> ContextAsync(Guid userId, EmbeddingSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.IsEmpty || moods.All.Count == 0)
            return null;

        var zone = TimeZoneInfo.FindSystemTimeZoneById((await settings.GetAsync(ct)).TimeZone);
        var daypart = Of(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Hour);

        var ranks = moods.All.Select(mood => moods.RanksIn(snapshot, mood)).ToArray();

        var lifts = await cache.GetOrCreateAsync((nameof(DaypartMoods), userId, daypart), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return Lifts(await PlaysAsync(userId, zone, snapshot, ranks, ct), ranks.Length, daypart);
        });

        if (lifts is null)
            return null;

        var context = new float[snapshot.Count];
        for (var mood = 0; mood < lifts.Length; mood++)
            if (lifts[mood] != 0)
                TensorPrimitives.MultiplyAdd(ranks[mood], (float)lifts[mood], context, context);

        return context;
    }

    // Подъём каждого настроения в части суток относительно всех частей; null — мало данных.
    public static double[]? Lifts(IReadOnlyList<(Daypart Daypart, int Mood, double Seconds)> plays, int moodCount, Daypart daypart)
    {
        if (plays.Count(play => play.Daypart == daypart) < MinimumDaypartPlays)
            return null;

        var overall = new double[moodCount];
        var inside = new double[moodCount];

        foreach (var (part, mood, seconds) in plays)
        {
            overall[mood] += seconds;
            if (part == daypart)
                inside[mood] += seconds;
        }

        var overallTotal = overall.Sum();
        var insideTotal = inside.Sum();
        if (overallTotal <= 0 || insideTotal <= 0)
            return null;

        return [.. Enumerable.Range(0, moodCount).Select(mood => inside[mood] / insideTotal - overall[mood] / overallTotal)];
    }

    private async Task<List<(Daypart, int, double)>> PlaysAsync(
        Guid userId, TimeZoneInfo zone, EmbeddingSnapshot snapshot, float[][] ranks, CancellationToken ct)
    {
        var since = clock.GetUtcNow() - Window;

        var events = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.TrackId != null && e.OccurredAt >= since
                        && (e.Type == PlaybackEventType.TrackCompleted || e.Type == PlaybackEventType.TrackSkipped)
                        && e.ListenedSeconds >= HistoryService.ThresholdSeconds)
            .Select(e => new { TrackId = e.TrackId!.Value, e.OccurredAt, e.ListenedSeconds })
            .ToListAsync(ct);

        var plays = new List<(Daypart, int, double)>(events.Count);

        foreach (var item in events)
        {
            var row = snapshot.RowOf(item.TrackId);
            if (row < 0)
                continue;

            var hour = TimeZoneInfo.ConvertTime(item.OccurredAt, zone).Hour;
            plays.Add((Of(hour), MoodCatalog.Dominant(ranks, row), item.ListenedSeconds));
        }

        return plays;
    }
}
