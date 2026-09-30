// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class TasteVectorFolder(
    IApplicationDbContext db,
    IEmbeddingIndex index)
{
    public async Task<UserTasteVector> LoadAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.UserTasteVectors.FirstOrDefaultAsync(vector => vector.UserId == userId, ct);
        if (existing is not null)
            return existing;

        var created = new UserTasteVector { UserId = userId, UpdatedAt = now };
        db.UserTasteVectors.Add(created);

        return created;
    }

    public void Apply(UserTasteVector target, PlaybackEvent playbackEvent, double completionRatio)
    {
        if (playbackEvent.TrackId is not { } trackId)
            return;

        var weight = TasteSignal.WeightFor(playbackEvent.Type, completionRatio);
        if (weight == 0)
            return;

        var snapshot = index.Snapshot();
        var row = snapshot.RowOf(trackId);
        if (row < 0)
            return;

        target.Vector = TasteVectorMath.Fold(target.Vector, snapshot.Vector(row), weight, RecommendationTuning.Vector.Alpha);
        target.Dimension = target.Vector.Length;
        target.UpdatedAt = playbackEvent.OccurredAt;

        if (weight > 0)
            target.PositiveCount++;
    }
}
