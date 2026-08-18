using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class Explorer
{
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

        var exploit = Diversifier.Select(familiar, exploitSlots, options, null, allowRelaxation: false);
        var explore = Diversifier.Select(
            novel, count - exploit.Count, options, exploit, allowRelaxation: false);

        TopUp(exploit, explore, candidates, count, options);

        return Interleave(exploit, explore, seed);
    }

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
