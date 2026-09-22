// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class Explorer
{
    /// <summary>Разнообразие внутри far-корзины: она и так узкая, держать её плотной незачем.</summary>
    private const double FarDiversityLambda = 0.55;

    /// <summary>Штраф за повтор артиста в far-корзине — сильнее обычного.</summary>
    private const double FarArtistRepeatPenalty = 0.20;

    public static List<RecommendationCandidate> Compose(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        double explorationRatio,
        ExplorationOptions exploration,
        DiversityOptions limits,
        int seed,
        IVectorSimilarity? vectors = null)
    {
        if (count <= 0 || candidates.Count == 0)
            return [];

        var (far, near) = Split(candidates, exploration, seed);

        var wantedExplore = Math.Min((int)Math.Ceiling(count * explorationRatio), far.Count);
        var exploitSlots = Math.Min(count - wantedExplore, near.Count);

        var exploit = Diversifier.Select(near, exploitSlots, limits, null, allowRelaxation: false, vectors);

        var explore = Diversifier.Select(far,
            count - exploit.Count,
            limits,
            exploit,
            allowRelaxation: false,
            vectors,
            diversityLambda: FarDiversityLambda,
            artistRepeatPenalty: FarArtistRepeatPenalty);

        TopUp(exploit, explore, candidates, count, limits, vectors);

        return Interleave(exploit, explore, seed);
    }

    /// <summary>
    /// Делит пул на «близкое» и «далёкое» по звучанию.
    /// <para>
    /// Far-корзина — это нижний квартиль по близости к вектору вкуса, то есть то, что
    /// действительно звучит иначе. Не <c>IsNovel</c> («не слышал и артист незнаком»): тот
    /// про новизну в каталоге, и по нему в exploration попадал бы очередной трек любимого
    /// жанра просто потому, что до него не дошли руки.
    /// </para>
    /// <para>
    /// Кандидат без эмбеддинга в far не попадает никогда: его близость к вкусу неизвестна, и
    /// назвать его «далёким от вашего вкуса» было бы неправдой.
    /// </para>
    /// </summary>
    private static (List<RecommendationCandidate> Far, List<RecommendationCandidate> Near) Split(
        IReadOnlyList<RecommendationCandidate> candidates,
        ExplorationOptions exploration,
        int seed)
    {
        var fits = candidates
            .Where(candidate => candidate.TasteFit is not null)
            .Select(candidate => (float)candidate.TasteFit!.Value)
            .ToArray();

        // Нет ни одного вектора — падаем на прежнее поведение: лучше новизна по каталогу,
        // чем никакой.
        if (fits.Length == 0)
        {
            var novel = new List<RecommendationCandidate>();
            var familiar = new List<RecommendationCandidate>();

            foreach (var candidate in candidates)
                (candidate.IsNovel ? novel : familiar).Add(candidate);

            return (novel, familiar);
        }

        var threshold = VectorMath.Quantile(fits, exploration.FarQuantile);

        var far = new List<RecommendationCandidate>();
        var near = new List<RecommendationCandidate>();
        var random = new Random(seed);

        foreach (var candidate in candidates)
        {
            if (candidate.TasteFit is { } fit && fit <= threshold)
            {
                // Внутри far ранжируем «от самого далёкого», с разбросом, чтобы корзина не была
                // одной и той же при каждой пересборке. Random сеян тем же ключом, что и
                // раскладка, поэтому полка воспроизводима в пределах дня.
                far.Add(candidate.WithScore(-fit + random.NextDouble() * exploration.FarJitter));
                continue;
            }

            near.Add(candidate);
        }

        return (far, near);
    }

    private static void TopUp(
        List<RecommendationCandidate> exploit,
        List<RecommendationCandidate> explore,
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        DiversityOptions limits,
        IVectorSimilarity? vectors)
    {
        var chosen = exploit.Concat(explore).ToList();
        var missing = count - chosen.Count;
        if (missing <= 0)
            return;

        var taken = chosen.Select(c => c.TrackId).ToHashSet();
        var remaining = candidates.Where(c => !taken.Contains(c.TrackId)).ToList();

        exploit.AddRange(Diversifier.Select(remaining, missing, limits, chosen, true, vectors));
    }

    private static List<RecommendationCandidate> Interleave(
        List<RecommendationCandidate> exploit,
        List<RecommendationCandidate> explore,
        int seed)
    {
        var total = exploit.Count + explore.Count;
        var result = new List<RecommendationCandidate>(total);

        if (explore.Count == 0)
            return exploit;

        if (exploit.Count == 0)
            return explore;

        var stride = (double)total / explore.Count;
        var offset = seed % Math.Max(1, (int)Math.Ceiling(stride));

        var novelPositions = new HashSet<int>();
        for (var index = 0; index < explore.Count; index++)
        {
            // Первым номером explore не ставим никогда: по первому треку слушатель судит
            // о всей полке, и «вот что-то совсем другое» — плохое первое впечатление.
            var position = Math.Clamp((int)(index * stride) + offset, 1, total - 1);
            novelPositions.Add(position);
        }

        var exploitQueue = new Queue<RecommendationCandidate>(exploit);
        var exploreQueue = new Queue<RecommendationCandidate>(explore);

        for (var position = 0; position < total; position++)
        {
            var wantsNovel = novelPositions.Contains(position) && exploreQueue.Count > 0;

            if (wantsNovel || exploitQueue.Count == 0)
                result.Add(exploreQueue.Count > 0 ? exploreQueue.Dequeue() : exploitQueue.Dequeue());
            else
                result.Add(exploitQueue.Dequeue());
        }

        return result;
    }

    public static int SeedFor(Guid userId, string shelfKey, DateTimeOffset now)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;

        foreach (var b in userId.ToByteArray())
            hash = (hash ^ b) * prime;

        foreach (var c in shelfKey)
            hash = (hash ^ (byte)c) * prime;

        foreach (var b in BitConverter.GetBytes(now.UtcDateTime.Date.Ticks))
            hash = (hash ^ b) * prime;

        return (int)(hash & int.MaxValue);
    }
}
