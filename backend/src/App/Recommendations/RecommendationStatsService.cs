// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Home;

namespace App.Recommendations;

public record SourceStatsDto(
    string? Source,
    bool Recommended,
    int Impressions,
    int Starts,
    int Completed,
    int Skipped,
    int SkippedEarly,
    int Liked,
    long ListenedSeconds);

public record FeatureWeightDto(string Name, double Hand, double? Learned);

// Чему движок научился на исходах этого слушателя: сколько примеров, какая доля у выученных весов.
public record RankingWeightsDto(int Examples, double Share, int MinimumExamples, IReadOnlyList<FeatureWeightDto> Features);

public record RecommendationStatsDto(
    int Days,
    long ListenedSeconds,
    long RecommendedSeconds,
    IReadOnlyList<SourceStatsDto> Sources,
    RankingWeightsDto Weights);

// Срабатывают ли рекомендации: по каждому источнику прослушивания (полка главной, радио, альбом…) —
// сколько раз полку показали, сколько треков с неё включили, дослушали, бросили сразу и лайкнули.
public class RecommendationStatsService(
    ApplicationDbContext db, ICurrentUser currentUser, PersonalWeights personalWeights, TimeProvider clock)
{
    public const int MaxDays = RecommendationTuning.Maintenance.EventRetentionDays;

    // Лайк засчитывается источнику, если трек лайкнули в течение суток после запуска оттуда.
    private static readonly TimeSpan LikeWindow = TimeSpan.FromDays(1);

    public async Task<RecommendationStatsDto> GetAsync(int days, CancellationToken ct)
    {
        if (days is < 1 or > MaxDays)
            throw new ValidationException($"Days must be between 1 and {MaxDays}.");

        var userId = currentUser.Id;
        var since = clock.GetUtcNow().AddDays(-days);

        var started = (int)PlaybackEventType.TrackStarted;
        var completed = (int)PlaybackEventType.TrackCompleted;
        var skipped = (int)PlaybackEventType.TrackSkipped;
        var liked = (int)PlaybackEventType.TrackLiked;
        var shown = (int)PlaybackEventType.ShelfShown;

        var rows = await db.Database.SqlQuery<SourceRow>(
                $"""
                SELECT source,
                       COUNT(*) FILTER (WHERE type = {shown})::int AS impressions,
                       COUNT(*) FILTER (WHERE type = {started})::int AS starts,
                       COUNT(*) FILTER (WHERE type = {completed})::int AS completed,
                       COUNT(*) FILTER (WHERE type = {skipped})::int AS skipped,
                       COUNT(*) FILTER (WHERE type = {skipped} AND listened_seconds < {Services.HistoryService.ThresholdSeconds})::int AS skipped_early,
                       COALESCE(SUM(GREATEST(listened_seconds, 0)) FILTER (WHERE type IN ({completed}, {skipped})), 0)::bigint AS listened_seconds
                FROM playback_events
                WHERE user_id = {userId} AND occurred_at >= {since}
                  AND type IN ({shown}, {started}, {completed}, {skipped})
                GROUP BY source
                """)
            .ToListAsync(ct);

        var likes = await db.Database.SqlQuery<LikeRow>(
                $"""
                SELECT s.source, COUNT(DISTINCT s.track_id)::int AS liked
                FROM playback_events s
                WHERE s.user_id = {userId} AND s.type = {started} AND s.occurred_at >= {since} AND s.source IS NOT NULL
                  AND EXISTS (
                      SELECT 1 FROM playback_events l
                      WHERE l.user_id = s.user_id AND l.track_id = s.track_id AND l.type = {liked}
                        AND l.occurred_at >= s.occurred_at AND l.occurred_at <= s.occurred_at + {LikeWindow})
                GROUP BY s.source
                """)
            .ToDictionaryAsync(row => row.Source, row => row.Liked, ct);

        var sources = rows
            .Where(row => row.Starts > 0 || row.Impressions > 0)
            .Select(row => new SourceStatsDto(
                row.Source,
                PlaybackSource.IsRecommendation(row.Source),
                row.Impressions,
                row.Starts,
                row.Completed,
                row.Skipped,
                row.SkippedEarly,
                row.Source is null ? 0 : likes.GetValueOrDefault(row.Source),
                row.ListenedSeconds))
            .OrderByDescending(row => row.ListenedSeconds)
            .ThenByDescending(row => row.Starts)
            .ThenBy(row => row.Source, StringComparer.Ordinal)
            .ToList();

        var personal = await personalWeights.LoadAsync(userId, ct);
        var hand = RankingWeights.Hand(ProfileMaturity.Mature);

        return new RecommendationStatsDto(
            days,
            sources.Sum(row => row.ListenedSeconds),
            sources.Where(row => row.Recommended).Sum(row => row.ListenedSeconds),
            sources,
            new RankingWeightsDto(
                personal.Examples,
                personal.Share,
                PersonalWeights.MinimumExamples,
                [.. RankingFeatures.Names.Select((name, feature) =>
                    new FeatureWeightDto(name, hand[feature], personal.Learned?[feature]))]));
    }

    private sealed record SourceRow(
        string? Source,
        int Impressions,
        int Starts,
        int Completed,
        int Skipped,
        int SkippedEarly,
        long ListenedSeconds);

    private sealed record LikeRow(string Source, int Liked);
}
