// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;

namespace App.Recommendations.Home;

// Полки собираются раз в несколько часов, а слушатель меняет настроение за минуты. При отдаче полка
// переставляется под текущую сессию: порядок движка остаётся основой, а близость к вектору сессии
// поднимает или опускает трек на несколько мест. Уже звучавшее в сессии с полки убирается.
public static class SessionRerank
{
    // Перевес сессии над исходным порядком: разница близостей в 0.3 сдвигает трек примерно на треть полки.
    private const double SessionWeight = 1.0;

    // Время суток (DaypartMoods) — подсказка мягче сессии: подъёмы настроений редко больше ±0.15,
    // так что трек сдвигается на одно-два места.
    private const double ContextWeight = 1.0;

    // Полки, которые следуют за вкусом. «Открытия» уводят от привычного, и сессия им не указ.
    private static readonly HashSet<string> Following = [ShelfKeys.ForYou, ShelfKeys.BecauseYouListened];

    public static bool Follows(string shelfKey) => Following.Contains(ShelfKeys.BaseOf(shelfKey));

    public static IReadOnlyList<CachedRecommendation> Apply(
        IReadOnlyList<CachedRecommendation> items, EmbeddingSnapshot snapshot, SessionState session, float[]? context = null)
    {
        if (context is not null && context.Length != snapshot.Count)
            context = null;

        if ((session.IsEmpty && context is null) || items.Count == 0)
            return items;

        var fresh = items
            .Where(item => item.Kind != RecommendedItemKind.Track || !session.Played.Contains(item.ItemId))
            .ToList();

        var vector = session.Vector is { } sessionVector && sessionVector.Length == snapshot.Dimension ? sessionVector : null;
        if (vector is null && context is null)
            return fresh;

        return
        [
            .. fresh
                .Select((item, position) =>
                {
                    var prior = 1.0 - (double)position / fresh.Count;
                    var row = item.Kind == RecommendedItemKind.Track ? snapshot.RowOf(item.ItemId) : -1;
                    var pull = row < 0 || vector is null ? 0 : TensorPrimitives.Dot(vector, snapshot.Vector(row));
                    var hint = row < 0 || context is null ? 0 : context[row];

                    return (Item: item, Position: position, Score: prior + SessionWeight * pull + ContextWeight * hint);
                })
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Position)
                .Select(entry => entry.Item),
        ];
    }
}
