// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class DjSessionService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    CandidateGenerator generator,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics)
{
    public const int DefaultBatchSize = 5;
    public const int MaxBatchSize = 20;

    private static readonly TimeSpan JustPlayed = TimeSpan.FromHours(24);
    private static readonly TimeSpan Forgotten = TimeSpan.FromDays(30);
    private static readonly TimeSpan DeepCut = TimeSpan.FromDays(180);

    private RecommendationOptions Options => options.Value;

    public async Task<DjBatchDto> GenerateAsync(DjRequest request, CancellationToken ct = default)
    {
        Validate(request);

        var userId = currentUser.Id;
        var now = clock.GetUtcNow();
        var context = await generator.LoadContextAsync(userId, now, ct);
        var seed = request.SeedTrackId;

        if (request.Mode == DjMode.Flow && seed is null)
            seed = LastPositiveTrack(context);

        var candidates = await CandidatesAsync(request.Mode, seed, context, ct);
        Score(candidates, context, request.Mode);

        var excluded = Excluded(request.Exclude, seed, context, now);
        var available = candidates.Where(candidate => !excluded.Contains(candidate.TrackId)).ToList();
        PrepareMode(request.Mode, available, context, now);

        var wanted = Math.Clamp(request.Limit ?? DefaultBatchSize, 1, MaxBatchSize);
        var picks = Pick(available, wanted, request.Mode, request.Variety, context, now);

        if (picks.Count < wanted && request.Mode == DjMode.Flow)
        {
            var taken = excluded.Concat(picks.Select(pick => pick.TrackId)).ToHashSet();
            var fallback = await generator.GenerateAsync(context, ct);
            Score(fallback, context, DjMode.ForYou);
            fallback.RemoveAll(candidate => taken.Contains(candidate.TrackId));

            picks.AddRange(Diversifier.Select(fallback, wanted - picks.Count, Options, picks));
        }

        var tracks = await db.TracksByIdAsync(userId, picks.Select(pick => pick.TrackId), ct);
        var result = picks
            .Where(pick => tracks.ContainsKey(pick.TrackId))
            .Select(pick => new RecommendedTrackDto(
                tracks[pick.TrackId],
                new RecommendationReasonDto(
                    pick.ReasonKind,
                    pick.ReasonSubject,
                    pick.ReasonSubjectId),
                null))
            .ToList();

        RecordImpressions(userId, request.Mode, result, now);
        if (result.Count > 0)
            await db.SaveChangesAsync(ct);

        metrics.RecordRequest($"dj:{request.Mode.ToString().ToLowerInvariant()}");
        metrics.RecordDjBatch(request.Mode.ToString(), result.Count);

        return new DjBatchDto(request.Mode, request.Variety, seed, result);
    }

    private void RecordImpressions(
        Guid userId,
        DjMode mode,
        IReadOnlyList<RecommendedTrackDto> tracks,
        DateTimeOffset now)
    {
        for (var position = 0; position < tracks.Count; position++)
        {
            db.RecommendationImpressions.Add(new RecommendationImpression
            {
                UserId = userId,
                TrackId = tracks[position].Track.Id,
                ShelfKey = $"dj:{mode.ToString().ToLowerInvariant()}",
                Position = position,
                ShownAt = now,
            });
        }

        metrics.RecordImpressions(tracks.Count, $"dj:{mode.ToString().ToLowerInvariant()}");
    }

    private async Task<List<RecommendationCandidate>> CandidatesAsync(
        DjMode mode,
        Guid? seed,
        UserRecommendationContext context,
        CancellationToken ct) => mode switch
        {
            DjMode.Rediscover => await generator.RediscoverAsync(context, ct),
            DjMode.Flow when seed is { } trackId => await generator.AroundAsync(context, trackId, ct),
            _ => await generator.GenerateAsync(context, ct),
        };

    private void Score(
        List<RecommendationCandidate> candidates, UserRecommendationContext context, DjMode mode)
    {
        var weights = Options.WeightsFor(context.Profile.Maturity);
        foreach (var candidate in candidates)
            DjSelectionPolicy.Score(candidate, context.Ranking, weights, Options, mode);
    }

    private static void PrepareMode(
        DjMode mode,
        List<RecommendationCandidate> candidates,
        UserRecommendationContext context,
        DateTimeOffset now)
    {
        if (mode == DjMode.Discover)
        {
            candidates.RemoveAll(candidate => context.Ranking.History.ContainsKey(candidate.TrackId));
            foreach (var candidate in candidates)
                candidate.ReasonKind = ReasonKinds.Discovery;
            return;
        }

        if (mode != DjMode.Rediscover)
            return;

        candidates.RemoveAll(candidate =>
            !context.Ranking.History.TryGetValue(candidate.TrackId, out var history)
            || history.Score <= 0);

        foreach (var candidate in candidates)
        {
            var history = context.Ranking.History[candidate.TrackId];
            candidate.IsNovel = now - history.LastPlayedAt >= DeepCut;
            candidate.ReasonKind = ReasonKinds.Rediscovery;
        }
    }

    private List<RecommendationCandidate> Pick(
        List<RecommendationCandidate> candidates,
        int wanted,
        DjMode mode,
        DjVariety variety,
        UserRecommendationContext context,
        DateTimeOffset now)
    {
        var ratio = DjSelectionPolicy.ExplorationRatio(variety);
        var seed = Explorer.SeedFor(context.UserId, $"dj:{mode}:{variety}", now);

        if (mode != DjMode.Rediscover)
            return Explorer.Compose(candidates, wanted, ratio, Options, seed);

        var forgotten = candidates
            .Where(candidate => now - context.Ranking.History[candidate.TrackId].LastPlayedAt >= Forgotten)
            .ToList();
        var picks = Explorer.Compose(forgotten, wanted, ratio, Options, seed);

        if (picks.Count < wanted)
        {
            var taken = picks.Select(pick => pick.TrackId).ToHashSet();
            var recent = candidates.Where(candidate => !taken.Contains(candidate.TrackId)).ToList();
            picks.AddRange(Diversifier.Select(recent, wanted - picks.Count, Options, picks));
        }

        return picks;
    }

    private static HashSet<Guid> Excluded(
        IReadOnlyList<Guid>? requested,
        Guid? seed,
        UserRecommendationContext context,
        DateTimeOffset now)
    {
        var excluded = new HashSet<Guid>(requested ?? []);
        if (seed is { } seedTrackId)
            excluded.Add(seedTrackId);

        var since = now - JustPlayed;
        foreach (var (trackId, history) in context.Ranking.History)
            if (history.LastPlayedAt >= since)
                excluded.Add(trackId);

        return excluded;
    }

    private static Guid? LastPositiveTrack(UserRecommendationContext context) =>
        context.Seeds.Select(seed => (Guid?)seed.TrackId).FirstOrDefault();

    private static void Validate(DjRequest request)
    {
        if (request.Mode is <= DjMode.Unknown or > DjMode.Flow)
            throw new ValidationException("Unknown DJ mode.");

        if (request.Variety is <= DjVariety.Unknown or > DjVariety.Adventurous)
            throw new ValidationException("Unknown DJ variety.");

        if (request.Limit is < 1 or > MaxBatchSize)
            throw new ValidationException($"DJ limit must be between 1 and {MaxBatchSize}.");
    }
}
