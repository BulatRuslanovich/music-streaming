// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations.Radio;

public class TransitionRecorder(IApplicationDbContext db)
{
    public static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(30);

    public async Task ApplyAsync(
        IReadOnlyList<PlaybackEvent> batch,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var edges = new Dictionary<(Guid From, Guid To), double>();

        var bySession = batch
            .Where(item => item.TrackId is not null && item.Type is PlaybackEventType.TrackStarted)
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
}
