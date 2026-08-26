// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.IntegrationTests.Evaluation;

public record SyntheticPlay(Guid TrackId, DateTimeOffset OccurredAt, bool Completed);

/// <summary>
/// Слушатель с латентным вкусом: почти всё время он в своей сцене, изредка заглядывает в соседние.
/// Генератор детерминирован — одно и то же зерно даёт одну и ту же историю, иначе оценка качества
/// мерила бы шум генератора, а не ранжирование.
/// </summary>
public static class SyntheticHistory
{
    private const double HomeShare = 0.85;
    private const double SkipShare = 0.15;

    public static List<SyntheticPlay> Generate(
        EvaluationCatalog catalog,
        EvaluationScene home,
        DateTimeOffset from,
        DateTimeOffset to,
        int seed,
        int playsPerDay = 5)
    {
        var random = new Random(seed);
        var elsewhere = catalog.Scenes.Where(scene => scene != home).ToList();

        // Вкус внутри сцены неравномерен, но не сосредоточен на пяти треках: иначе в отложенном
        // окне не осталось бы ни одного трека, который человек слышит впервые.
        var preference = home.TrackIds
            .OrderBy(_ => random.Next())
            .Select((trackId, index) => (trackId, weight: 1.0 / (1 + index * 0.05)))
            .ToList();

        var total = preference.Sum(entry => entry.weight);
        var plays = new List<SyntheticPlay>();
        var days = Math.Max(1, (int)(to - from).TotalDays);

        for (var day = 0; day < days; day++)
        {
            var midnight = from.AddDays(day);

            for (var play = 0; play < playsPerDay; play++)
            {
                var at = midnight.AddMinutes(random.Next(8 * 60, 23 * 60));

                var trackId = random.NextDouble() < HomeShare || elsewhere.Count == 0
                    ? PickWeighted(random, preference, total)
                    : PickAny(random, elsewhere[random.Next(elsewhere.Count)]);

                plays.Add(new SyntheticPlay(trackId, at, random.NextDouble() >= SkipShare));
            }
        }

        return plays.OrderBy(play => play.OccurredAt).ToList();
    }

    public static List<PlaybackEvent> ToEvents(
        Guid userId, IEnumerable<SyntheticPlay> plays, IReadOnlyDictionary<Guid, int> durations)
    {
        var events = new List<PlaybackEvent>();
        var session = Guid.CreateVersion7();
        var previous = DateTimeOffset.MinValue;

        foreach (var play in plays)
        {
            var duration = durations.TryGetValue(play.TrackId, out var seconds) ? seconds : 200;

            // Новая сессия, когда между треками прошло больше получаса: на этом держится
            // ко-встречаемость, которую считает SimilarityMaintenance.
            if (play.OccurredAt - previous > TimeSpan.FromMinutes(30))
                session = Guid.CreateVersion7();

            previous = play.OccurredAt;

            events.Add(new PlaybackEvent
            {
                UserId = userId,
                TrackId = play.TrackId,
                Type = play.Completed ? PlaybackEventType.TrackCompleted : PlaybackEventType.TrackSkipped,
                OccurredAt = play.OccurredAt,
                ListenedSeconds = play.Completed ? duration : Math.Max(1, duration / 20),
                DurationSeconds = duration,
                PositionSeconds = play.Completed ? duration : Math.Max(1, duration / 20),
                SessionId = session,
                Source = PlaybackSource.Home,
                Platform = "web",
            });
        }

        return events;
    }

    private static Guid PickWeighted(
        Random random, IReadOnlyList<(Guid TrackId, double Weight)> preference, double total)
    {
        var target = random.NextDouble() * total;

        foreach (var (trackId, weight) in preference)
        {
            target -= weight;
            if (target <= 0)
                return trackId;
        }

        return preference[^1].TrackId;
    }

    private static Guid PickAny(Random random, EvaluationScene scene) =>
        scene.TrackIds[random.Next(scene.TrackIds.Count)];
}
