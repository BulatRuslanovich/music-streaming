// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Сворачивает пачку событий в векторы вкуса — общий и по части суток.
/// <para>
/// Живёт внутри того же прохода, что двигает watermark профиля, поэтому каждое событие
/// учитывается ровно один раз. Делать это в <c>EventIngestWorker</c> было бы соблазнительно
/// ради отзывчивости, но там watermark'а нет вовсе: повторённая пачка удвоила бы вклад,
/// а при alpha = 0.22 удвоение это заметный сдвиг вкуса, а не погрешность.
/// </para>
/// </summary>
public class TasteVectorFolder(
    IApplicationDbContext db,
    IEmbeddingIndex index,
    IOptions<RecommendationOptions> options)
{
    private RecommendationOptions Options => options.Value;

    /// <summary>Загружает векторы пользователя, создавая недостающие.</summary>
    public async Task<TasteVectorSet> LoadAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.UserTasteVectors
            .Where(vector => vector.UserId == userId)
            .ToDictionaryAsync(vector => vector.Context, ct);

        var timeZone = Dayparts.ZoneOrUtc(await db.UserSettings.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => item.TimeZone)
            .FirstOrDefaultAsync(ct));

        foreach (var context in TasteContexts.All)
        {
            if (existing.ContainsKey(context))
                continue;

            var created = new UserTasteVector { UserId = userId, Context = context, UpdatedAt = now };
            db.UserTasteVectors.Add(created);
            existing[context] = created;
        }

        return new TasteVectorSet(existing, timeZone);
    }

    /// <summary>
    /// Применяет одно событие к общему вектору и к вектору его части суток.
    /// <para>
    /// Трек без эмбеддинга не вносит ничего и <b>не увеличивает счётчик</b>: иначе слушатель
    /// дошёл бы до зрелого вектора, который на самом деле ничего не впитал.
    /// </para>
    /// </summary>
    public void Apply(TasteVectorSet vectors, PlaybackEvent playbackEvent, double completionRatio)
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

        var trackVector = snapshot.Vector(row);
        var daypart = TasteContexts.For(Dayparts.Of(playbackEvent.OccurredAt, vectors.TimeZone));

        Fold(vectors.Of(TasteContext.Global), trackVector, weight, playbackEvent.OccurredAt);
        Fold(vectors.Of(daypart), trackVector, weight, playbackEvent.OccurredAt);
    }

    private void Fold(
        UserTasteVector target,
        ReadOnlySpan<float> trackVector,
        double weight,
        DateTimeOffset at)
    {
        target.Vector = TasteVectorMath.Fold(target.Vector, trackVector, weight, Options.TasteAlpha);
        target.Dimension = target.Vector.Length;
        target.UpdatedAt = at;

        if (weight > 0)
            target.PositiveCount++;
        else
            target.NegativeCount++;
    }
}

/// <summary>Векторы одного слушателя вместе с его часовым поясом.</summary>
public sealed class TasteVectorSet(
    Dictionary<TasteContext, UserTasteVector> vectors,
    TimeZoneInfo timeZone)
{
    public TimeZoneInfo TimeZone { get; } = timeZone;

    public UserTasteVector Of(TasteContext context) => vectors[context];

    public UserTasteVector Global => vectors[TasteContext.Global];
}
