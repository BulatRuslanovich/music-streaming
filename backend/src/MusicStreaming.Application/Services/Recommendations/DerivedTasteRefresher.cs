// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Пересчёт производных полей профиля: итоги, топы, вкус по годам и по частям суток. Отдельный
/// проход после свёртки событий — считается один раз в конце, а не на каждое событие.
/// </summary>
public class DerivedTasteRefresher(IApplicationDbContext db, IOptions<RecommendationOptions> options)
{
    private const int DaypartGenreCount = 5;

    private RecommendationOptions Options => options.Value;

    public async Task RefreshAsync(UserTasteProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var userId = profile.UserId;

        var totals = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tracks = g.Count(),
                CompletionSum = g.Sum(a => a.CompletionSum),
                CompletionSamples = g.Sum(a => a.CompletionSamples),
                Plays = g.Sum(a => a.PlayCount),
                Skips = g.Sum(a => a.SkipCount),
            })
            .FirstOrDefaultAsync(ct);

        profile.DistinctTracks = totals?.Tracks ?? 0;
        profile.AverageCompletion = totals is { CompletionSamples: > 0 }
            ? totals.CompletionSum / totals.CompletionSamples
            : 0;
        profile.SkipRate = totals is { Plays: > 0 } ? (double)totals.Skips / totals.Plays : 0;

        profile.TopArtists = await db.UserArtistAffinities.AsNoTracking()
            .Where(a => a.UserId == userId && a.Score > 0)
            .OrderByDescending(a => a.Score)
            .Take(20)
            .Select(a => new TasteEntry(a.ArtistId, a.Artist!.Name, a.Score))
            .ToListAsync(ct);

        profile.TopGenres = await db.UserGenreAffinities.AsNoTracking()
            .Where(a => a.UserId == userId && a.Score > 0)
            .OrderByDescending(a => a.Score)
            .Take(10)
            .Select(a => new TasteEntry(a.GenreId, a.Genre!.Name, a.Score))
            .ToListAsync(ct);

        await RefreshYearTasteAsync(profile, ct);
        await RefreshDaypartTasteAsync(profile, now, ct);

        profile.Maturity = AffinityMath.MaturityFor(
            RecencyDecay.ValueAt(
                profile.PositiveSignalMass, profile.SignalDecayAnchor, now, Options.ProfileHalfLifeDays),
            Options.WarmThreshold,
            Options.MatureThreshold);

        profile.UpdatedAt = now;
    }

    private async Task RefreshYearTasteAsync(UserTasteProfile profile, CancellationToken ct)
    {
        var years = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == profile.UserId && a.Score > 0 && a.Track!.Year != null)
            .Select(a => new { Year = a.Track!.Year!.Value, a.Score })
            .ToListAsync(ct);

        if (years.Count == 0)
        {
            profile.YearCenter = null;
            profile.YearSpread = 0;
            return;
        }

        var totalWeight = years.Sum(y => y.Score);
        if (totalWeight <= 0)
        {
            profile.YearCenter = null;
            profile.YearSpread = 0;
            return;
        }

        var center = years.Sum(y => y.Year * y.Score) / totalWeight;
        var variance = years.Sum(y => y.Score * Math.Pow(y.Year - center, 2)) / totalWeight;

        profile.YearCenter = center;
        profile.YearSpread = Math.Sqrt(variance);
    }

    /// <summary>
    /// Вкус по частям суток. Час прослушивания хранится в UTC, а вечер у человека свой, поэтому
    /// раскладка идёт по местному времени из его настроек.
    /// </summary>
    private async Task RefreshDaypartTasteAsync(
        UserTasteProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-Options.DaypartWindowDays);

        var rows = await db.ListeningStats.AsNoTracking()
            .Where(stat => stat.UserId == profile.UserId && stat.Hour >= since && stat.ListenedSeconds > 0)
            .Select(stat => new
            {
                stat.Hour,
                stat.ListenedSeconds,
                stat.Track!.GenreId,
                GenreName = stat.Track.Genre == null ? null : stat.Track.Genre.Name,
                Energy = stat.Track.AudioFeatures != null && stat.Track.AudioFeatures.Succeeded
                    ? (double?)stat.Track.AudioFeatures.Energy
                    : null,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            profile.Dayparts = [];
            return;
        }

        var timeZone = Dayparts.ZoneOrUtc(await db.UserSettings.AsNoTracking()
            .Where(item => item.UserId == profile.UserId)
            .Select(item => item.TimeZone)
            .FirstOrDefaultAsync(ct));

        var total = rows.Sum(row => (double)row.ListenedSeconds);
        var tastes = new List<DaypartTaste>(Dayparts.All.Count);

        foreach (var part in Dayparts.All)
        {
            var inside = rows.Where(row => Dayparts.Of(row.Hour, timeZone) == part).ToList();
            var seconds = inside.Sum(row => (double)row.ListenedSeconds);

            if (seconds <= 0)
                continue;

            var withEnergy = inside.Where(row => row.Energy is not null).ToList();
            var energyWeight = withEnergy.Sum(row => (double)row.ListenedSeconds);

            var genres = inside
                .Where(row => row.GenreId is not null)
                .GroupBy(row => (Id: row.GenreId!.Value, Name: row.GenreName ?? string.Empty))
                .Select(group => new TasteEntry(
                    group.Key.Id,
                    group.Key.Name,
                    group.Sum(row => (double)row.ListenedSeconds) / seconds))
                .OrderByDescending(entry => entry.Score)
                .Take(DaypartGenreCount)
                .ToList();

            tastes.Add(new DaypartTaste(
                part,
                seconds / total,
                energyWeight <= 0
                    ? null
                    : withEnergy.Sum(row => row.Energy!.Value * row.ListenedSeconds) / energyWeight,
                genres));
        }

        profile.Dayparts = tastes;
    }
}
