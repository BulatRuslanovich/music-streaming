// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations.Embeddings;

public class TasteVectorFolder(
    IApplicationDbContext db,
    EmbeddingIndex index)
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
