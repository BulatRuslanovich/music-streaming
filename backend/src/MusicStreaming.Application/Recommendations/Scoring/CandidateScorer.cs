// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

public record RankingContext(
    IReadOnlyDictionary<Guid, double> ArtistScores,
    IReadOnlyDictionary<Guid, double> GenreScores,
    IReadOnlyDictionary<Guid, TrackHistory> History,
    IReadOnlyDictionary<Guid, DateTimeOffset> LastShown,
    DateTimeOffset Now,
    double? YearCenter = null,
    double YearSpread = 0)
{
    public static RankingContext Empty(DateTimeOffset now) =>
        new(new Dictionary<Guid, double>(), new Dictionary<Guid, double>(),
            new Dictionary<Guid, TrackHistory>(), new Dictionary<Guid, DateTimeOffset>(), now);
}

public record TrackHistory(
    DateTimeOffset LastPlayedAt,
    int PlayCount,
    int SkipCount,
    double AverageCompletion,
    double Score,
    int CompletedCount = 0,
    int ReplayCount = 0,
    int PlaylistAdds = 0);

public static class CandidateScorer
{
    public static void Score(
        RecommendationCandidate candidate,
        RankingContext context,
        RankingWeights weights,
        CandidatePenaltyOptions penalties)
    {
        candidate.Behavior = BehaviorScore(candidate, context);

        var merit = weights.Combine(
            candidate.Content,
            candidate.TasteFit,
            candidate.AudioSimilarity,
            candidate.Collaborative,
            candidate.Behavior,
            candidate.Popularity,
            candidate.Freshness,
            candidate.Coverage);

        var confirmations = Math.Clamp(candidate.EvidenceCount - 1, 0, 3);
        var consensus = 1 + confirmations * penalties.MultiSourceBonus;

        candidate.Score = merit * consensus * PenaltyFor(candidate, context, penalties);
    }

    /// <summary>
    /// Аффинити по всем указанным артистам, а не по одному «самому сильному по модулю»:
    /// иначе один нелюбимый приглашённый артист топил трек любимого основного.
    /// </summary>
    public static double BehaviorScore(RecommendationCandidate candidate, RankingContext context)
    {
        var total = 0.0;
        var weight = 0.0;

        foreach (var artistId in candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId])
        {
            if (!context.ArtistScores.TryGetValue(artistId, out var score))
                continue;

            var share = artistId == candidate.ArtistId ? 1.0 : 0.5;

            // Негатив приглашённого артиста звучит вполовину тише: он реже определяет трек.
            if (score < 0 && artistId != candidate.ArtistId)
                share *= 0.5;

            total += score * share;
            weight += share;
        }

        var artist = weight > 0 ? total / weight : 0;

        var genre = candidate.GenreId is { } genreId && context.GenreScores.TryGetValue(genreId, out var g)
            ? g
            : 0;

        return Math.Clamp(artist * 0.7 + genre * 0.3, -1, 1);
    }

    public static double PenaltyFor(
        RecommendationCandidate candidate,
        RankingContext context,
        CandidatePenaltyOptions penalties)
    {
        var penalty = 1.0;

        if (context.History.TryGetValue(candidate.TrackId, out var history))
        {
            var sinceLastPlay = context.Now - history.LastPlayedAt;

            if (sinceLastPlay < TimeSpan.FromHours(penalties.JustPlayedHours))
                penalty *= penalties.JustPlayed;
            else if (sinceLastPlay < TimeSpan.FromDays(penalties.RecentlyPlayedDays))
                penalty *= penalties.RecentlyPlayed;

            if (history is { SkipCount: >= 2, AverageCompletion: < 0.2 })
                penalty *= penalties.DislikedTrack;
        }

        if (context.LastShown.TryGetValue(candidate.TrackId, out var shownAt)
            && context.Now - shownAt < TimeSpan.FromDays(penalties.ImpressionCooldownDays))
        {
            penalty *= penalties.UnclickedImpression;
        }

        if (candidate.Behavior < -0.3)
            penalty *= penalties.DislikedArtist;

        penalty *= QualityFactor(candidate, penalties);
        penalty *= EraFactor(candidate, context, penalties);

        return penalty;
    }

    /// <summary>Трек, который бросает вся библиотека, не должен попадать в подборки наравне с прочими.</summary>
    public static double QualityFactor(RecommendationCandidate candidate, CandidatePenaltyOptions penalties)
    {
        if (candidate.GlobalSkipRate is not { } skipRate)
            return 1;

        var threshold = penalties.HighSkipRateThreshold;
        if (skipRate <= threshold || threshold >= 1)
            return 1;

        var excess = Math.Clamp((skipRate - threshold) / (1 - threshold), 0, 1);

        return 1 - (1 - penalties.HighSkipRatePenalty) * excess;
    }

    /// <summary>Мягкое соответствие эпохе, которую слушает пользователь (<see cref="RankingContext.YearCenter"/>).</summary>
    public static double EraFactor(
        RecommendationCandidate candidate, RankingContext context, CandidatePenaltyOptions penalties)
    {
        if (context.YearCenter is not { } center || candidate.Year is not { } year)
            return 1;

        var spread = Math.Max(context.YearSpread, penalties.MinimumYearSpread);
        if (spread <= 0)
            return 1;

        var distance = (year - center) / spread;
        var fit = Math.Exp(-0.5 * distance * distance);

        return penalties.EraFitFloor + (1 - penalties.EraFitFloor) * fit;
    }
}
