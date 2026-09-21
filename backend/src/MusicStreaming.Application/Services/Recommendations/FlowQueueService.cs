// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Queue;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Очередь радио поверх пространства эмбеддингов: работа с базой вокруг чистого
/// <see cref="QueueBuilder"/>.
/// <para>
/// Серверной сессии здесь нет намеренно. Очередью владеет клиент, а API остаётся без состояния;
/// на сервер переехало только то, чего клиент знать не может: что слушатель играл последние двое
/// суток и какой трек взять якорем, когда его не назвали.
/// </para>
/// </summary>
public class FlowQueueService(
    IApplicationDbContext db,
    IEmbeddingIndex index,
    TasteVectorReader tasteVectors,
    IOptions<RecommendationOptions> options)
{
    /// <summary>Окно истории, из которого засевается список «уже слышал».</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(48);

    private const int RecentLimit = 120;

    /// <summary>Сколько кандидатов рассматривается при выборе якоря.</summary>
    private const int AnchorCandidates = 20;

    /// <summary>
    /// Температура выбора якоря. Низкая: почти всегда берётся что-то из верхушки, но не одно
    /// и то же каждый раз.
    /// </summary>
    private const double AnchorTemperature = 0.08;

    /// <summary>Штраф якорю за то, что трек звучал недавно.</summary>
    private const double AnchorRecencyPenalty = 0.35;

    /// <summary>С какой вероятностью якорь берётся совсем случайно — чтобы не запереться в углу.</summary>
    private const double AnchorRandomChance = 0.12;

    private RecommendationOptions Options => options.Value;

    /// <summary>Готов ли путь: без эмбеддингов очередь строить не из чего.</summary>
    public bool IsReady => index.IsReady;

    public async Task<FlowQueue> BuildAsync(
        Guid userId,
        Guid? seedTrackId,
        IReadOnlyCollection<Guid> clientExclude,
        int size,
        DateTimeOffset now,
        bool discover,
        CancellationToken ct)
    {
        var snapshot = index.Snapshot();
        if (snapshot.IsEmpty)
            return FlowQueue.Empty;

        var taste = await tasteVectors.CurrentAsync(userId, now, snapshot, ct);
        var exclude = await ExcludeAsync(userId, clientExclude, snapshot, now, ct);

        var random = new Random(Explorer.SeedFor(userId, "radio", now) ^ (int)(now.Ticks & 0xFFFF));
        var anchorRow = AnchorRow(snapshot, taste, seedTrackId, exclude, random);

        // Якорь тоже не должен вернуться в очередь.
        if (anchorRow >= 0)
        {
            foreach (var clone in snapshot.CloneIds(snapshot.MetaAt(anchorRow).TrackId))
                exclude.Add(clone);
        }

        var maturity = discover ? VectorMaturityLevel.Discovering : taste.Maturity;

        var request = new QueueRequest(
            CurrentRow: anchorRow,
            Taste: taste.Query,
            Exclude: exclude,
            ExploreRatio: VectorMaturity.EffectiveExplore(
                Options.QueueExploreRatio, Options.QueueDiscoverExploreRatio, maturity),
            Discover: maturity == VectorMaturityLevel.Discovering,
            TransitionsFrom: await TransitionsAsync(snapshot, anchorRow, ct),
            Size: size,
            Now: now,
            Seed: random.Next());

        var items = QueueBuilder.Build(snapshot, request, Options);
        var anchorId = anchorRow >= 0 ? snapshot.MetaAt(anchorRow).TrackId : (Guid?)null;

        return new FlowQueue(anchorId, items);
    }

    /// <summary>
    /// «Уже слышал» — это объединение того, что прислал клиент, и того, что реально звучало
    /// за двое суток, расширенное до клонов: иначе тот же трек вернулся бы под другим файлом.
    /// </summary>
    private async Task<HashSet<Guid>> ExcludeAsync(
        Guid userId,
        IReadOnlyCollection<Guid> clientExclude,
        EmbeddingSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var since = now - RecentWindow;

        var recent = await db.PlaybackEvents.AsNoTracking()
            .Where(item => item.UserId == userId && item.TrackId != null && item.OccurredAt >= since)
            .GroupBy(item => item.TrackId!.Value)
            .OrderByDescending(group => group.Max(item => item.OccurredAt))
            .Take(RecentLimit)
            .Select(group => group.Key)
            .ToListAsync(ct);

        var exclude = new HashSet<Guid>(clientExclude);

        foreach (var trackId in recent.Concat(clientExclude))
            exclude.UnionWith(snapshot.CloneIds(trackId));

        return exclude;
    }

    private async Task<IReadOnlyDictionary<Guid, double>> TransitionsAsync(
        EmbeddingSnapshot snapshot, int anchorRow, CancellationToken ct)
    {
        if (anchorRow < 0)
            return new Dictionary<Guid, double>();

        var from = snapshot.MetaAt(anchorRow).TrackId;

        return await db.TrackTransitions.AsNoTracking()
            .Where(transition => transition.FromTrackId == from && transition.Weight >= 1)
            .OrderByDescending(transition => transition.Weight)
            .Take(200)
            .ToDictionaryAsync(transition => transition.ToTrackId, transition => transition.Weight, ct);
    }

    /// <summary>
    /// С чего начать, когда трек не назвали. Не просто «самое любимое»: из верхушки берётся
    /// мягкая выборка со штрафом за недавнее звучание, плюс небольшой шанс уйти совсем в сторону.
    /// Иначе радио каждый раз начиналось бы с одного и того же трека.
    /// </summary>
    private static int AnchorRow(
        EmbeddingSnapshot snapshot,
        TasteQuery taste,
        Guid? seedTrackId,
        IReadOnlySet<Guid> exclude,
        Random random)
    {
        if (seedTrackId is { } seed && snapshot.RowOf(seed) is var seeded and >= 0)
            return seeded;

        if (!taste.IsReady)
            return -1;

        if (random.NextDouble() < AnchorRandomChance && snapshot.Count > 8)
            return random.Next(snapshot.Count);

        var candidates = snapshot.TopK(taste.Query, AnchorCandidates);
        if (candidates.Count == 0)
            return -1;

        var scores = candidates
            .Select(hit => hit.Score - (exclude.Contains(hit.TrackId) ? AnchorRecencyPenalty : 0))
            .ToArray();

        // Если недавним оказалось всё, штраф ничего не упорядочивает — возвращаемся к чистым оценкам.
        if (candidates.All(hit => exclude.Contains(hit.TrackId)))
        {
            for (var i = 0; i < scores.Length; i++)
                scores[i] = candidates[i].Score;
        }

        var best = scores.Max();
        var weights = scores.Select(score => Math.Exp((score - best) / AnchorTemperature)).ToArray();
        var threshold = random.NextDouble() * weights.Sum();

        for (var i = 0; i < weights.Length; i++)
        {
            threshold -= weights[i];
            if (threshold <= 0)
                return candidates[i].Row;
        }

        return candidates[0].Row;
    }
}

/// <param name="AnchorTrackId">С какого трека очередь оттолкнулась; null — якоря не нашлось.</param>
public record FlowQueue(Guid? AnchorTrackId, IReadOnlyList<QueueItem> Items)
{
    public static FlowQueue Empty { get; } = new(null, []);
}
