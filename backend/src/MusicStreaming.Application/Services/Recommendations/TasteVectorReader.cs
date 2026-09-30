// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public record TasteQuery(float[] Query, VectorMaturityLevel Maturity, int PositiveCount)
{
    public bool IsReady => Query.Length > 0;

    public static TasteQuery Empty { get; } = new([], VectorMaturityLevel.Discovering, 0);
}

public class TasteVectorReader(
    IApplicationDbContext db)
{
    private const int MaxPendingEvents = 200;

    public async Task<TasteQuery> CurrentAsync(
        Guid userId,
        EmbeddingSnapshot snapshot,
        CancellationToken ct = default)
    {
        if (snapshot.IsEmpty)
            return TasteQuery.Empty;

        var stored = await db.UserTasteVectors.AsNoTracking()
            .FirstOrDefaultAsync(vector => vector.UserId == userId, ct);

        if (stored is null)
            return TasteQuery.Empty;

        var vector = stored.Vector;
        var positiveCount = stored.PositiveCount;

        var watermark = await db.UserTasteProfiles.AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => profile.EventsWatermark)
            .FirstOrDefaultAsync(ct);

        var pending = await db.PlaybackEvents.AsNoTracking()
            .Where(item => item.UserId == userId && item.Sequence > watermark && item.TrackId != null)
            .OrderBy(item => item.Sequence)
            .Take(MaxPendingEvents)
            .Select(item => new
            {
                item.Type,
                item.TrackId,
                item.ListenedSeconds,
                item.DurationSeconds,
            })
            .ToListAsync(ct);

        foreach (var item in pending)
        {
            var weight = TasteSignal.WeightFor(
                item.Type, EventWeights.CompletionRatio(item.ListenedSeconds, item.DurationSeconds));

            if (weight == 0)
                continue;

            var row = snapshot.RowOf(item.TrackId!.Value);
            if (row < 0)
                continue;

            vector = TasteVectorMath.Fold(vector, snapshot.Vector(row), weight, RecommendationTuning.Vector.Alpha);

            if (weight > 0)
                positiveCount++;
        }

        if (vector.Length == 0)
            return TasteQuery.Empty;

        return new TasteQuery(
            vector,
            VectorMaturity.Of(positiveCount, RecommendationTuning.Vector.FormingAt, RecommendationTuning.Vector.ReadyAt),
            positiveCount);
    }
}
