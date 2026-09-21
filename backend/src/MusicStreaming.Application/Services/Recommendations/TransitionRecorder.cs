// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Копит граф «какой трек шёл сразу за каким».
/// <para>
/// Считается внутри того же прохода, что двигает watermark профиля, — иначе повторённая пачка
/// удвоила бы вес рёбер. Учитываются только соседние события одной сессии и только когда между
/// ними не больше получаса: после долгой паузы следующий трек выбирает уже не предыдущий,
/// а человек заново.
/// </para>
/// </summary>
public class TransitionRecorder(IApplicationDbContext db)
{
    /// <summary>Разрыв, после которого соседство перестаёт что-либо значить.</summary>
    public static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Собирает рёбра внутри пачки и начисляет их.
    /// <para>
    /// Стык между пачками теряется: последнее событие одной и первое следующей ребром не
    /// становятся. При пачке в две тысячи событий это одно потерянное ребро на две тысячи —
    /// на фоне веса, который копится месяцами, разницы нет, а хранить хвост пачки ради этого
    /// значило бы завести ещё одно состояние рядом с watermark.
    /// </para>
    /// </summary>
    public async Task ApplyAsync(
        IReadOnlyList<PlaybackEvent> batch,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var edges = new Dictionary<(Guid From, Guid To), double>();
        var bySession = batch
            .Where(item => item.TrackId is not null && StartsAPlay(item.Type))
            .GroupBy(item => item.SessionId);

        foreach (var session in bySession)
        {
            var ordered = session.OrderBy(item => item.Sequence).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                var from = ordered[index - 1];
                var to = ordered[index];

                if (from.TrackId == to.TrackId)
                    continue;

                if (to.OccurredAt - from.OccurredAt > MaximumGap)
                    continue;

                var edge = (from.TrackId!.Value, to.TrackId!.Value);
                edges[edge] = edges.GetValueOrDefault(edge) + 1.0;
            }
        }

        if (edges.Count == 0)
            return;

        var touched = edges.Keys.Select(edge => edge.From).Distinct().ToList();

        var existing = await db.TrackTransitions
            .Where(transition => touched.Contains(transition.FromTrackId))
            .ToDictionaryAsync(transition => (transition.FromTrackId, transition.ToTrackId), ct);

        foreach (var ((from, to), weight) in edges)
        {
            if (existing.TryGetValue((from, to), out var transition))
            {
                transition.Weight += weight;
                transition.UpdatedAt = now;
                continue;
            }

            db.TrackTransitions.Add(new TrackTransition
            {
                FromTrackId = from,
                ToTrackId = to,
                Weight = weight,
                UpdatedAt = now,
            });
        }
    }

    /// <summary>
    /// Начало прослушивания. Только оно: завершение и пропуск относятся к тому же треку,
    /// что и старт, и учитывать их значило бы считать одно соседство трижды.
    /// </summary>
    private static bool StartsAPlay(PlaybackEventType type) => type is PlaybackEventType.TrackStarted;
}
