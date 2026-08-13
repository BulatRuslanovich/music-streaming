using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Резервирует часть каждой полки под музыку за пределами устоявшегося вкуса пользователя.
///
/// <para>
/// Рекомендатель, который только эксплуатирует уже известное, сходится ко всё более узкому кругу:
/// он никогда не узнает, что человеку понравился бы жанр, который тот просто не успел включить, и
/// полки становятся затхлыми. Отвести четверть слотов новизне — самое дешёвое из доступных
/// лекарств, и вдобавок единственный способ, которым холодный исполнитель или только что
/// загруженный жанр вообще получают первое прослушивание.
/// </para>
/// </summary>
public static class Explorer
{
    /// <summary>
    /// Разбивает оценённый пул на полку, смешивая знакомое и новое.
    ///
    /// <para>
    /// Детерминировано для конкретного пользователя, полки и дня: обновление страницы не должно
    /// перетасовывать музыку под курсором читателя, но завтрашняя полка не должна быть точной
    /// копией сегодняшней.
    /// </para>
    /// </summary>
    public static List<RecommendationCandidate> Compose(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        double explorationRatio,
        RecommendationOptions options,
        int seed)
    {
        if (count <= 0 || candidates.Count == 0)
            return [];

        var novel = new List<RecommendationCandidate>();
        var familiar = new List<RecommendationCandidate>();

        foreach (var candidate in candidates)
            (candidate.IsNovel ? novel : familiar).Add(candidate);

        var wantedExplore = Math.Min((int)Math.Ceiling(count * explorationRatio), novel.Count);
        var exploitSlots = Math.Min(count - wantedExplore, familiar.Count);

        // Ни один из проходов не вправе нарушить квоту ради своей доли. У слушателя, который
        // слушает исключительно двух исполнителей, знакомый пул узкий, и позволить эксплуатации
        // заполнять слоты из него любой ценой означало бы поставить на полку четыре трека одного
        // артиста — ровно тот повтор, ради которого квоты и существуют. Слоты, которые она не
        // может заполнить честно, уходят исследованию.
        var exploit = Diversifier.Select(familiar, exploitSlots, options, null, allowRelaxation: false);
        var explore = Diversifier.Select(
            novel, count - exploit.Count, options, exploit, allowRelaxation: false);

        TopUp(exploit, explore, candidates, count, options);

        return Interleave(exploit, explore, seed);
    }

    /// <summary>
    /// Добирает то, что оба пула оставили пустым, — на этот раз позволяя квотам уступить.
    ///
    /// <para>
    /// Сюда доходит только тогда, когда любой оставшийся кандидат повторил бы что-то уже стоящее
    /// на полке: маленькая библиотека или слушатель, прошедший её почти целиком. Однообразная
    /// полка лучше полупустой, но это последнее средство, а не первое.
    /// </para>
    /// </summary>
    private static void TopUp(
        List<RecommendationCandidate> exploit,
        List<RecommendationCandidate> explore,
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        RecommendationOptions options)
    {
        var chosen = exploit.Concat(explore).ToList();
        var missing = count - chosen.Count;
        if (missing <= 0)
            return;

        var taken = chosen.Select(c => c.TrackId).ToHashSet();
        var remaining = candidates.Where(c => !taken.Contains(c.TrackId)).ToList();

        exploit.AddRange(Diversifier.Select(remaining, missing, options, chosen));
    }

    /// <summary>
    /// Распределяет новинки по всей полке, вместо того чтобы складывать их в конец, где
    /// горизонтальная прокрутка их прячет.
    /// </summary>
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

        // Позиции для новинок, расставленные равномерно и сдвинутые сидом, чтобы рисунок не был
        // одинаковым для каждого пользователя и каждого дня.
        var stride = (double)total / explore.Count;
        var offset = seed % Math.Max(1, (int)Math.Ceiling(stride));

        var novelPositions = new HashSet<int>();
        for (var index = 0; index < explore.Count; index++)
            novelPositions.Add(Math.Min(total - 1, (int)(index * stride) + offset));

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

    /// <summary>
    /// Сид, устойчивый к перезапускам — в отличие от <c>GetHashCode</c> — и меняющийся раз в сутки.
    /// </summary>
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
