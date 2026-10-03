// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations;

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

        var years = await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == userId && a.Score > 0 && a.Track!.Year != null)
            .Select(a => new { Year = a.Track!.Year!.Value, a.Score })
            .ToListAsync(ct);

        var totalWeight = years.Sum(y => y.Score);
        if (totalWeight > 0)
        {
            var center = years.Sum(y => y.Year * y.Score) / totalWeight;
            profile.YearCenter = center;
            profile.YearSpread = Math.Sqrt(years.Sum(y => y.Score * Math.Pow(y.Year - center, 2)) / totalWeight);
        }
        else
        {
            profile.YearCenter = null;
            profile.YearSpread = 0;
        }

        var positiveSignals = RecencyDecay.ValueAt(
            profile.PositiveSignalMass, profile.SignalDecayAnchor, now, RecommendationTuning.Decay.ProfileHalfLifeDays);

        profile.Maturity = positiveSignals >= RecommendationTuning.Decay.MatureThreshold ? ProfileMaturity.Mature
            : positiveSignals >= RecommendationTuning.Decay.WarmThreshold ? ProfileMaturity.Warm
            : ProfileMaturity.Cold;

        profile.UpdatedAt = now;
    }
}
