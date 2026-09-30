// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Queue;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class FlowQueueService(
    IApplicationDbContext db,
    IEmbeddingIndex index,
    TasteVectorReader tasteVectors)
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(48);

    private const int RecentLimit = 120;

    private const int AnchorCandidates = 20;

    private const double AnchorTemperature = 0.08;

    private const double AnchorRecencyPenalty = 0.35;

    private const double AnchorRandomChance = 0.12;

    public bool IsReady => index.IsReady;

    public async Task<FlowQueue> BuildAsync(
        Guid userId,
        Guid? seedTrackId,
        IReadOnlyCollection<Guid> clientExclude,
        int size,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        if (snapshot.IsEmpty)
            return FlowQueue.Empty;

        var taste = await tasteVectors.CurrentAsync(userId, snapshot, ct);

        var since = now - RecentWindow;
        var recent = await db.PlaybackEvents.AsNoTracking()
            .Where(item => item.UserId == userId && item.TrackId != null && item.OccurredAt >= since)
            .GroupBy(item => item.TrackId!.Value)
            .OrderByDescending(group => group.Max(item => item.OccurredAt))
            .Take(RecentLimit)
            .Select(group => group.Key)
            .ToListAsync(ct);

        var exclude = new HashSet<Guid>(clientExclude);
        foreach (var trackId in recent.Concat(clientExclude))
            exclude.UnionWith(snapshot.CloneIds(trackId));

        var random = new Random(Explorer.SeedFor(userId, "radio", now) ^ (int)(now.Ticks & 0xFFFF));
        var anchorRow = AnchorRow(snapshot, taste, seedTrackId, exclude, random);

        var anchorId = anchorRow >= 0 ? snapshot.MetaAt(anchorRow).TrackId : (Guid?)null;
        IReadOnlyDictionary<Guid, double> transitions = new Dictionary<Guid, double>();

        if (anchorId is { } from)
        {
            exclude.UnionWith(snapshot.CloneIds(from));

            transitions = await db.TrackTransitions.AsNoTracking()
                .Where(transition => transition.FromTrackId == from && transition.Weight >= 1)
                .OrderByDescending(transition => transition.Weight)
                .Take(200)
                .ToDictionaryAsync(transition => transition.ToTrackId, transition => transition.Weight, ct);
        }

        var request = new QueueRequest(
            CurrentRow: anchorRow,
            Taste: taste.Query,
            Exclude: exclude,
            ExploreRatio: VectorMaturity.EffectiveExplore(
                RecommendationTuning.Exploration.QueueRatio, RecommendationTuning.Exploration.QueueDiscoverRatio, taste.Maturity),
            TransitionsFrom: transitions,
            Size: size,
            Now: now,
            Seed: random.Next());

        return new FlowQueue(anchorId, QueueBuilder.Build(snapshot, request));
    }

    private static int AnchorRow(
        EmbeddingSnapshot snapshot,
        TasteQuery taste,
        Guid? seedTrackId,
        IReadOnlySet<Guid> exclude,
        Random random)
    {
        if (seedTrackId is { } seed && snapshot.RowOf(seed) is var seeded and >= 0)
            return seeded;

        if (!taste.IsReady)
            return -1;

        if (random.NextDouble() < AnchorRandomChance && snapshot.Count > 8)
            return random.Next(snapshot.Count);

        var candidates = snapshot.TopK(taste.Query, AnchorCandidates);
        if (candidates.Count == 0)
            return -1;

        var scores = candidates
            .Select(hit => hit.Score - (exclude.Contains(hit.TrackId) ? AnchorRecencyPenalty : 0))
            .ToArray();

        if (candidates.All(hit => exclude.Contains(hit.TrackId)))
        {
            for (var i = 0; i < scores.Length; i++)
                scores[i] = candidates[i].Score;
        }

        var best = scores.Max();
        var weights = scores.Select(score => Math.Exp((score - best) / AnchorTemperature)).ToArray();
        var threshold = random.NextDouble() * weights.Sum();

        for (var i = 0; i < weights.Length; i++)
        {
            threshold -= weights[i];
            if (threshold <= 0)
                return candidates[i].Row;
        }

        return candidates[0].Row;
    }
}

public record FlowQueue(Guid? AnchorTrackId, IReadOnlyList<QueueItem> Items)
{
    public static FlowQueue Empty { get; } = new(null, []);
}
