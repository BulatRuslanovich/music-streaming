// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;

namespace App.Recommendations.Home;

// Пример для обучения: признаки рекомендации на момент показа и что с ней стало.
public readonly record struct RankingExample(float[] Features, bool Liked);

public sealed record PersonalWeightsResult(RankingWeights? Learned, int Examples, double Share)
{
    // Ручные веса уступают выученным по мере накопления примеров; результат снова в сумме единица.
    public RankingWeights Apply(RankingWeights hand)
    {
        if (Learned is null || Share <= 0)
            return hand;

        var blended = hand.Values.Select((value, index) => (1 - Share) * value + Share * Learned.Values[index]).ToArray();
        var total = blended.Sum();

        return total > 0 ? new RankingWeights([.. blended.Select(value => value / total)]) : hand;
    }
}

// Персональные веса признаков ранжирования. Каждый запуск трека с рекомендательной полки — пример:
// признаки кандидата (сохранены в кеше полки и переписаны в событие старта) и исход — дослушал или
// бросил в начале. По ним учится логистическая регрессия, притянутая к ручным весам: пока примеров
// мало, она почти не отходит от них, и доля выученных весов растёт как n / (n + HalfConfidence).
public class PersonalWeights(ApplicationDbContext db, TimeProvider clock)
{
    // Столько размеченных исходов дают выученным весам половинную долю.
    public const int HalfConfidence = 200;

    // Меньше — выученные веса не применяются вовсе.
    public const int MinimumExamples = 30;

    private static readonly TimeSpan Window = TimeSpan.FromDays(RecommendationTuning.Maintenance.EventRetentionDays);

    // Конец прослушивания ищется в той же сессии не позже этого после старта.
    private static readonly TimeSpan OutcomeWindow = TimeSpan.FromHours(2);

    // Полка кеша, с которой мог быть запущен трек из этого источника; null — старты отсюда не учат.
    // Радио свои очереди собирает иначе (QueueBuilder).
    private static string? ShelfFor(string? source) => source switch
    {
        "home:forYou" => ShelfKeys.ForYou,
        "home:discover" => ShelfKeys.Discover,
        "home:becauseYouListened" => ShelfKeys.BecauseYouListened,
        "home:dailyMix" or "mix:daily" => ShelfKeys.MixPool,
        _ => null,
    };

    public static bool ShelfServes(string? source, string shelfKey) =>
        ShelfFor(source) is { } shelf && ShelfKeys.BaseOf(shelfKey) == shelf;

    public static float[] Encode(double?[] features) =>
        [.. features.Select(value => value is { } known ? (float)known : float.NaN)];

    // Старт с рекомендательной полки получает признаки, с которыми трек стоял на полке: вместе с исходом
    // прослушивания это пример для обучения.
    public async Task AttachFeaturesAsync(Guid userId, IReadOnlyList<PlaybackEvent> events)
    {
        var starts = events
            .Where(e => e.Type == PlaybackEventType.TrackStarted && e.TrackId is not null && ShelfFor(e.Source) is not null)
            .ToList();

        if (starts.Count == 0)
            return;

        // У «Потому что вы слушали» ключ полки с суффиксом затравки: «becauseYouListened:<id>».
        var wanted = starts.Select(start => ShelfFor(start.Source)!).Distinct().ToList();
        var prefixed = wanted.Contains(ShelfKeys.BecauseYouListened);

        var shelves = await db.RecommendationCache.AsNoTracking()
            .Where(entry => entry.UserId == userId
                            && (wanted.Contains(entry.ShelfKey)
                                || (prefixed && entry.ShelfKey.StartsWith(ShelfKeys.BecauseYouListened + ":"))))
            .Select(entry => new { entry.ShelfKey, entry.Payload })
            .ToListAsync();

        foreach (var start in starts)
        {
            var features = shelves
                .Where(shelf => ShelfServes(start.Source, shelf.ShelfKey))
                .SelectMany(shelf => shelf.Payload)
                .FirstOrDefault(item => item.ItemId == start.TrackId && item.Features is not null)
                ?.Features;

            if (features is not null)
                start.Features = Encode(features);
        }
    }

    public async Task<PersonalWeightsResult> LoadAsync(Guid userId, CancellationToken ct)
    {
        var examples = await ExamplesAsync(userId, ct);
        if (examples.Count < MinimumExamples)
            return new PersonalWeightsResult(null, examples.Count, 0);

        var prior = RankingWeights.Hand(ProfileMaturity.Mature);
        var learned = Fit(examples, prior);

        return new PersonalWeightsResult(learned, examples.Count, examples.Count / (double)(examples.Count + HalfConfidence));
    }

    // Регуляризованная логистическая регрессия: веса притянуты к prior (ручным весам), отрицательные
    // обнуляются — признак, который у этого слушателя не помогает, просто не учитывается.
    public static RankingWeights Fit(IReadOnlyList<RankingExample> examples, RankingWeights prior)
    {
        const int Iterations = 400;
        const double LearningRate = 0.5;
        const double Pull = 0.05;
        // Ручные веса в сумме единица, а логиту нужен размах: масштаб начальной точки и притяжения.
        const double Scale = 4;

        var count = RankingFeatures.Count;
        // Неизвестный признак (NaN) ни за, ни против: считается нулём.
        var values = examples
            .Select(example => Enumerable.Range(0, count)
                .Select(feature => feature < example.Features.Length && !float.IsNaN(example.Features[feature])
                    ? example.Features[feature]
                    : 0.0)
                .ToArray())
            .ToArray();
        var anchor = prior.Values.Select(value => value * Scale).ToArray();
        var weights = anchor.ToArray();
        var bias = 0.0;

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var gradient = new double[count];
            var biasGradient = 0.0;

            for (var index = 0; index < examples.Count; index++)
            {
                var logit = bias;
                for (var feature = 0; feature < count; feature++)
                    logit += weights[feature] * values[index][feature];

                var error = 1 / (1 + Math.Exp(-logit)) - (examples[index].Liked ? 1 : 0);
                biasGradient += error;

                for (var feature = 0; feature < count; feature++)
                    gradient[feature] += error * values[index][feature];
            }

            bias -= LearningRate * biasGradient / examples.Count;

            for (var feature = 0; feature < count; feature++)
            {
                var step = gradient[feature] / examples.Count + Pull * (weights[feature] - anchor[feature]);
                weights[feature] -= LearningRate * step;
            }
        }

        var positive = weights.Select(weight => Math.Max(0, weight)).ToArray();
        var total = positive.Sum();

        return total > 0 ? new RankingWeights([.. positive.Select(weight => weight / total)]) : prior;
    }

    // Старты с рекомендательных полок с признаками и их исход: дослушал (или дослушал больше половины) —
    // понравилось, бросил в первые 20 % — нет; середина ничего не говорит и не учит.
    private async Task<List<RankingExample>> ExamplesAsync(Guid userId, CancellationToken ct)
    {
        var since = clock.GetUtcNow() - Window;

        var starts = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.Type == PlaybackEventType.TrackStarted
                        && e.Features != null && e.TrackId != null && e.OccurredAt >= since)
            .Select(e => new { TrackId = e.TrackId!.Value, e.SessionId, e.OccurredAt, Features = e.Features! })
            .ToListAsync(ct);

        if (starts.Count == 0)
            return [];

        var sessions = starts.Select(start => start.SessionId).Distinct().ToList();
        var ends = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && sessions.Contains(e.SessionId) && e.TrackId != null && e.OccurredAt >= since
                        && (e.Type == PlaybackEventType.TrackCompleted || e.Type == PlaybackEventType.TrackSkipped))
            .Select(e => new { TrackId = e.TrackId!.Value, e.SessionId, e.OccurredAt, e.Type, e.ListenedSeconds, e.DurationSeconds })
            .ToListAsync(ct);

        var endsByPlay = ends
            .GroupBy(end => (end.TrackId, end.SessionId))
            .ToDictionary(group => group.Key, group => group.OrderBy(end => end.OccurredAt).ToList());

        var examples = new List<RankingExample>(starts.Count);

        foreach (var start in starts)
        {
            if (!endsByPlay.TryGetValue((start.TrackId, start.SessionId), out var candidates))
                continue;

            var end = candidates.FirstOrDefault(item =>
                item.OccurredAt >= start.OccurredAt && item.OccurredAt - start.OccurredAt <= OutcomeWindow);
            if (end is null)
                continue;

            var ratio = EventWeights.CompletionRatio(end.ListenedSeconds, end.DurationSeconds);
            bool? liked = end.Type == PlaybackEventType.TrackCompleted || ratio >= 0.5 ? true
                : ratio < 0.2 ? false
                : null;

            if (liked is { } outcome)
                examples.Add(new RankingExample(start.Features, outcome));
        }

        return examples;
    }
}
