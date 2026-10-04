// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Recommendations.Embeddings;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations;

public readonly record struct RecommendationSeed(Guid TrackId, double Weight);

public record UserRecommendationContext(
    Guid UserId,
    ProfileMaturity Maturity,
    IReadOnlyList<Guid> TopArtistIds,
    string? TopArtistName,
    RankingContext Ranking,
    IReadOnlyList<RecommendationSeed> Seeds,
    float[] Taste)
{
    private const int SeedTrackCount = 20;

    private static readonly TimeSpan SeedRecencyHalfLife = TimeSpan.FromDays(30);

    public static async Task<UserRecommendationContext> LoadAsync(
        ApplicationDbContext db, EmbeddingSnapshot snapshot, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        // Зрелость профиля — по затухшему числу положительных сигналов.
        var signals = await db.UserTasteProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.PositiveSignalMass, p.SignalDecayAnchor })
            .FirstOrDefaultAsync(ct);

        var positiveSignals = signals is null
            ? 0
            : RecencyDecay.ValueAt(signals.PositiveSignalMass, signals.SignalDecayAnchor, now, RecommendationTuning.Decay.ProfileHalfLifeDays);

        var maturity = positiveSignals >= RecommendationTuning.Decay.MatureThreshold ? ProfileMaturity.Mature
            : positiveSignals >= RecommendationTuning.Decay.WarmThreshold ? ProfileMaturity.Warm
            : ProfileMaturity.Cold;

        // Affinity затухает до момента чтения: давно не слушанное честно теряет вес.
        var artistScores = (await db.UserArtistAffinities.AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => new { a.ArtistId, a.DecayedWeight, a.DecayAnchor })
                .ToListAsync(ct))
            .ToDictionary(
                a => a.ArtistId,
                a => RecencyDecay.Score(a.DecayedWeight, a.DecayAnchor, now, RecommendationTuning.Decay.ArtistHalfLifeDays));

        var genreScores = (await db.UserGenreAffinities.AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => new { a.GenreId, a.DecayedWeight, a.DecayAnchor })
                .ToListAsync(ct))
            .ToDictionary(
                a => a.GenreId,
                a => RecencyDecay.Score(a.DecayedWeight, a.DecayAnchor, now, RecommendationTuning.Decay.GenreHalfLifeDays));

        var history = (await db.UserTrackAffinities.AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => new
                {
                    a.TrackId,
                    a.LastPlayedAt,
                    a.PlayCount,
                    a.CompletedCount,
                    a.SkipCount,
                    a.ReplayCount,
                    a.PlaylistAdds,
                    a.CompletionSum,
                    a.CompletionSamples,
                    a.DecayedWeight,
                    a.DecayAnchor,
                    a.Track!.Year,
                })
                .ToListAsync(ct))
            .ToDictionary(
                h => h.TrackId,
                h => new TrackHistory(
                    h.LastPlayedAt,
                    h.PlayCount,
                    h.SkipCount,
                    h.CompletionSamples == 0 ? 0 : h.CompletionSum / h.CompletionSamples,
                    RecencyDecay.Score(h.DecayedWeight, h.DecayAnchor, now, RecommendationTuning.Decay.TrackHalfLifeDays),
                    h.CompletedCount,
                    h.ReplayCount,
                    h.PlaylistAdds,
                    h.Year));

        // Привычная эпоха: средний год понравившихся треков и разброс вокруг него.
        var liked = history.Values.Where(h => h.Score > 0 && h.Year is not null).ToList();
        var yearWeight = liked.Sum(h => h.Score);
        double? yearCenter = yearWeight > 0 ? liked.Sum(h => h.Year!.Value * h.Score) / yearWeight : null;
        var yearSpread = yearCenter is { } center
            ? Math.Sqrt(liked.Sum(h => h.Score * Math.Pow(h.Year!.Value - center, 2)) / yearWeight)
            : 0;


        var topArtistIds = artistScores.Where(a => a.Value > 0).OrderByDescending(a => a.Value).Take(3).Select(a => a.Key).ToList();
        var topArtistName = topArtistIds.Count == 0
            ? null
            : await db.Artists.AsNoTracking().Where(a => a.Id == topArtistIds[0]).Select(a => a.Name).FirstOrDefaultAsync(ct);

        // Затравки: недавние треки, которые реально зашли (дослушаны, переиграны, добавлены в плейлист).
        var seeds = new List<RecommendationSeed>();

        foreach (var (trackId, track) in history)
        {
            if (track.Score <= 0 || (track.SkipCount >= 2 && track.AverageCompletion < 0.20 && track.Score < 0.35))
                continue;

            var engagement = Math.Max(
                Math.Clamp(track.AverageCompletion, 0, 1),
                Math.Max(
                    track.CompletedCount > 0 ? 0.85 : 0,
                    Math.Max(track.ReplayCount > 0 ? 0.95 : 0, track.PlaylistAdds > 0 ? 1 : 0)));

            engagement = Math.Max(engagement, Math.Clamp(track.Score * 2, 0, 1));

            var repetition = 1 - Math.Exp(-Math.Max(1, track.PlayCount) / 3.0);
            var age = Math.Max(0, (now - track.LastPlayedAt).TotalSeconds);
            var recency = Math.Pow(0.5, age / SeedRecencyHalfLife.TotalSeconds);

            var weight = track.Score
                         * (0.35 + 0.45 * engagement + 0.20 * repetition)
                         * (0.45 + 0.55 * recency);

            if (weight > 0)
                seeds.Add(new RecommendationSeed(trackId, weight));
        }

        seeds = [.. seeds.OrderByDescending(seed => seed.Weight).ThenBy(seed => seed.TrackId).Take(SeedTrackCount)];

        // Вкус в пространстве звучания: центр затравок, взвешенный их силой, с отталкиванием
        // от явно нелюбимого. Считается заново при каждом чтении, поэтому всегда согласован с affinity.
        float[] taste = [];
        if (!snapshot.IsEmpty && seeds.Any(seed => snapshot.RowOf(seed.TrackId) >= 0))
        {
            taste = new float[snapshot.Dimension];

            var pulls = seeds.Select(seed => (seed.TrackId, seed.Weight))
                .Concat(history.Where(h => h.Value.Score < 0).Select(h => (TrackId: h.Key, Weight: h.Value.Score)));

            foreach (var (trackId, weight) in pulls)
            {
                var row = snapshot.RowOf(trackId);
                if (row < 0)
                    continue;

                var vector = snapshot.Vector(row);
                for (var i = 0; i < taste.Length; i++)
                    taste[i] += (float)weight * vector[i];
            }

            VectorMath.NormalizeInPlace(taste);
        }

        return new UserRecommendationContext(
            userId,
            maturity,
            topArtistIds,
            topArtistName,
            new RankingContext(artistScores, genreScores, history, now, yearCenter, yearSpread),
            seeds,
            taste);
    }
}

public record RankingContext(
    IReadOnlyDictionary<Guid, double> ArtistScores,
    IReadOnlyDictionary<Guid, double> GenreScores,
    IReadOnlyDictionary<Guid, TrackHistory> History,
    DateTimeOffset Now,
    double? YearCenter = null,
    double YearSpread = 0);

public record TrackHistory(
    DateTimeOffset LastPlayedAt,
    int PlayCount,
    int SkipCount,
    double AverageCompletion,
    double Score,
    int CompletedCount = 0,
    int ReplayCount = 0,
    int PlaylistAdds = 0,
    int? Year = null);
