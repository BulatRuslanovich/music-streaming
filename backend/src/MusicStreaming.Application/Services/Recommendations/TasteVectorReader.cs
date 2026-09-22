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

/// <param name="Query">Чем спрашивать индекс: общий вкус, смешанный с частью суток. Пустой — вкуса ещё нет.</param>
/// <param name="Maturity">Насколько вектору можно доверять.</param>
/// <param name="PositiveCount">Сколько положительных сигналов он впитал.</param>
public record TasteQuery(float[] Query, VectorMaturityLevel Maturity, int PositiveCount)
{
    public bool IsReady => Query.Length > 0;

    public static TasteQuery Empty { get; } = new([], VectorMaturityLevel.Discovering, 0);
}

/// <summary>
/// Отдаёт вектор запроса «вкус слушателя сейчас».
/// <para>
/// Свёртка векторов идёт в фоновом проходе раз в минуту, и для полок этого достаточно — они
/// всё равно кэшируются на часы. Радио же отвечает на нажатие кнопки, поэтому здесь события,
/// не попавшие в персистентный вектор, досворачиваются <b>в памяти</b>, без записи: watermark
/// остаётся за фоновым проходом, и продублировать вклад нечем.
/// </para>
/// </summary>
public class TasteVectorReader(
    IApplicationDbContext db,
    IOptions<RecommendationOptions> options)
{
    /// <summary>
    /// Потолок догоняемых событий. Индекс (UserId, Sequence) уже есть, но запрос всё равно
    /// должен быть ограничен: отставание в тысячи событий — повод подождать фоновый проход,
    /// а не считать всё в обработчике запроса.
    /// </summary>
    private const int MaxPendingEvents = 200;

    private RecommendationOptions Options => options.Value;

    public async Task<TasteQuery> CurrentAsync(
        Guid userId,
        DateTimeOffset now,
        EmbeddingSnapshot snapshot,
        CancellationToken ct = default)
    {
        if (snapshot.IsEmpty)
            return TasteQuery.Empty;

        var stored = await db.UserTasteVectors.AsNoTracking()
            .Where(vector => vector.UserId == userId)
            .ToDictionaryAsync(vector => vector.Context, ct);

        if (stored.Count == 0)
            return TasteQuery.Empty;

        var timeZone = Dayparts.ZoneOrUtc(await db.UserSettings.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => item.TimeZone)
            .FirstOrDefaultAsync(ct));

        var daypartContext = TasteContexts.For(Dayparts.Of(now, timeZone));

        var global = stored.GetValueOrDefault(TasteContext.Global);
        var daypart = stored.GetValueOrDefault(daypartContext);

        var globalVector = global?.Vector ?? [];
        var daypartVector = daypart?.Vector ?? [];
        var positiveCount = global?.PositiveCount ?? 0;

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
                item.OccurredAt,
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

            var trackVector = snapshot.Vector(row);
            globalVector = TasteVectorMath.Fold(globalVector, trackVector, weight, Options.Vector.Alpha);

            if (TasteContexts.For(Dayparts.Of(item.OccurredAt, timeZone)) == daypartContext)
                daypartVector = TasteVectorMath.Fold(daypartVector, trackVector, weight, Options.Vector.Alpha);

            if (weight > 0)
                positiveCount++;
        }

        if (globalVector.Length == 0)
            return TasteQuery.Empty;

        // Часть суток сдвигает запрос, но не подменяет его: вечером человек остаётся собой.
        var share = (float)Options.Vector.DaypartBlendShare;
        var query = VectorMath.Blend(globalVector, share, daypartVector, 1 - share);

        return new TasteQuery(
            query,
            VectorMaturity.Of(positiveCount, Options.Vector.FormingAt, Options.Vector.ReadyAt),
            positiveCount);
    }
}
