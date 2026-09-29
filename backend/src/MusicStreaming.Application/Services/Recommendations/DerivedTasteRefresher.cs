// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Пересчёт производных полей профиля: топы, вкус по годам, зрелость. Отдельный
/// проход после свёртки событий — считается один раз в конце, а не на каждое событие.
/// </summary>
public class DerivedTasteRefresher(IApplicationDbContext db)
{
    public async Task RefreshAsync(UserTasteProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var userId = profile.UserId;

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

        profile.Maturity = AffinityMath.MaturityFor(
            RecencyDecay.ValueAt(
                profile.PositiveSignalMass, profile.SignalDecayAnchor, now, RecommendationTuning.Decay.ProfileHalfLifeDays),
            RecommendationTuning.Decay.WarmThreshold,
            RecommendationTuning.Decay.MatureThreshold);

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
}
