// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;

namespace App.Recommendations;

// Что слушатель делает прямо сейчас. Vector — направление в пространстве звучания: дослушанное и
// лайкнутое за последние минуты тянет к себе, брошенное в начале и отвергнутое отталкивает, свежее весит
// больше. Played — что уже звучало в эти минуты. null-вектор — сессии нет или звучало то, чего нет в индексе.
public sealed record SessionState(float[]? Vector, IReadOnlySet<Guid> Played)
{
    public static SessionState Empty { get; } = new(null, new HashSet<Guid>());

    public bool IsEmpty => Vector is null && Played.Count == 0;
}

public class SessionTaste(ApplicationDbContext db)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(45);

    private const double HalfLifeMinutes = 15;

    public async Task<SessionState> LoadAsync(Guid userId, EmbeddingSnapshot snapshot, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - Window;

        var events = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.TrackId != null && e.OccurredAt >= since
                        && (e.Type == PlaybackEventType.TrackStarted
                            || e.Type == PlaybackEventType.TrackCompleted
                            || e.Type == PlaybackEventType.TrackSkipped
                            || e.Type == PlaybackEventType.TrackReplayed
                            || e.Type == PlaybackEventType.TrackLiked
                            || e.Type == PlaybackEventType.TrackUnliked
                            || e.Type == PlaybackEventType.TrackDismissed))
            .Select(e => new { TrackId = e.TrackId!.Value, e.Type, e.OccurredAt, e.ListenedSeconds, e.DurationSeconds, e.Source })
            .ToListAsync(ct);

        if (events.Count == 0)
            return SessionState.Empty;

        var played = events.Where(e => e.Type == PlaybackEventType.TrackStarted).Select(e => e.TrackId).ToHashSet();
        float[]? vector = null;

        foreach (var item in events)
        {
            if (item.Type == PlaybackEventType.TrackStarted)
                continue;

            var row = snapshot.RowOf(item.TrackId);
            if (row < 0)
                continue;

            var weight = item.Type switch
            {
                PlaybackEventType.TrackCompleted or PlaybackEventType.TrackReplayed => 1.0,
                PlaybackEventType.TrackLiked => 1.5,
                PlaybackEventType.TrackUnliked => -1.5,
                PlaybackEventType.TrackDismissed => -2.0,
                _ => EventWeights.CompletionRatio(item.ListenedSeconds, item.DurationSeconds) switch
                {
                    < 0.20 => -1.0,
                    < 0.50 => -0.3,
                    _ => 0.3,
                },
            };

            weight = EventWeights.WithSkipSource(item.Type, weight, item.Source);

            var age = Math.Max(0, (now - item.OccurredAt).TotalMinutes);
            weight *= Math.Pow(0.5, age / HalfLifeMinutes);

            vector ??= new float[snapshot.Dimension];
            TensorPrimitives.MultiplyAdd(snapshot.Vector(row), (float)weight, vector, vector);
        }

        if (vector is not null)
            VectorMath.NormalizeInPlace(vector);

        return new SessionState(vector, played);
    }
}
