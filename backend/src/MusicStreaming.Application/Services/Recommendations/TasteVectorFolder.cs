// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Сворачивает пачку событий в вектор вкуса слушателя.
/// <para>
/// Живёт внутри того же прохода, что двигает watermark профиля, поэтому каждое событие
/// учитывается ровно один раз. Делать это в <c>EventIngestWorker</c> было бы соблазнительно
/// ради отзывчивости, но там watermark'а нет вовсе: повторённая пачка удвоила бы вклад,
/// а при alpha = 0.22 удвоение это заметный сдвиг вкуса, а не погрешность.
/// </para>
/// </summary>
public class TasteVectorFolder(
    IApplicationDbContext db,
    IEmbeddingIndex index)
{
    /// <summary>Загружает вектор пользователя, создавая его при первом сигнале.</summary>
    public async Task<UserTasteVector> LoadAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.UserTasteVectors.FirstOrDefaultAsync(vector => vector.UserId == userId, ct);
        if (existing is not null)
            return existing;

        var created = new UserTasteVector { UserId = userId, UpdatedAt = now };
        db.UserTasteVectors.Add(created);

        return created;
    }

    /// <summary>
    /// Применяет одно событие к вектору.
    /// <para>
    /// Трек без эмбеддинга не вносит ничего и <b>не увеличивает счётчик</b>: иначе слушатель
    /// дошёл бы до зрелого вектора, который на самом деле ничего не впитал.
    /// </para>
    /// </summary>
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

        // Считаем только положительные: зрелость вектора — это «сколько он впитал», а не
        // «сколько раз его дёрнули». Отрицательный сигнал направление меняет, доверия не добавляет.
        if (weight > 0)
            target.PositiveCount++;
    }
}
