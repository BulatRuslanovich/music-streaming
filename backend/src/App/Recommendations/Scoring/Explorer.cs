// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Recommendations.Embeddings;

namespace App.Recommendations.Scoring;

public static class Explorer
{
    private const double FarDiversityLambda = 0.55;

    private const double FarArtistRepeatPenalty = 0.20;

    public static List<RecommendationCandidate> Compose(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        double explorationRatio,
        int seed,
        IVectorSimilarity? vectors = null)
    {
        if (count <= 0 || candidates.Count == 0)
            return [];

        var (far, near) = Split(candidates, seed);

        var wantedExplore = Math.Min((int)Math.Ceiling(count * explorationRatio), far.Count);
        var exploitSlots = Math.Min(count - wantedExplore, near.Count);

        var exploit = Diversifier.Select(near, exploitSlots, null, allowRelaxation: false, vectors);

        var explore = Diversifier.Select(far,
            count - exploit.Count,
            exploit,
            allowRelaxation: false,
            vectors,
            diversityLambda: FarDiversityLambda,
            artistRepeatPenalty: FarArtistRepeatPenalty);

        var chosen = exploit.Concat(explore).ToList();
        if (count <= chosen.Count) return Interleave(exploit, explore, seed);
        var taken = chosen.Select(c => c.TrackId).ToHashSet();
        var remaining = candidates.Where(c => !taken.Contains(c.TrackId)).ToList();

        exploit.AddRange(Diversifier.Select(remaining, count - chosen.Count, chosen, true, vectors));

        return Interleave(exploit, explore, seed);
    }

    private static (List<RecommendationCandidate> Far, List<RecommendationCandidate> Near) Split(
        IReadOnlyList<RecommendationCandidate> candidates,
        int seed)
    {
        var fits = candidates
            .Where(candidate => candidate.TasteFit is not null)
            .Select(candidate => (float)candidate.TasteFit!.Value)
            .ToArray();

        if (fits.Length == 0)
        {
            var novel = new List<RecommendationCandidate>();
            var familiar = new List<RecommendationCandidate>();

            foreach (var candidate in candidates)
                (candidate.IsNovel ? novel : familiar).Add(candidate);

            return (novel, familiar);
        }

        var threshold = VectorMath.Quantile(fits, RecommendationTuning.Exploration.FarQuantile);

        var far = new List<RecommendationCandidate>();
        var near = new List<RecommendationCandidate>();
        var random = new Random(seed);

        foreach (var candidate in candidates)
        {
            if (candidate.TasteFit is { } fit && fit <= threshold)
            {
                far.Add(candidate.WithScore(-fit + random.NextDouble() * RecommendationTuning.Exploration.FarJitter));
                continue;
            }

            near.Add(candidate);
        }

        return (far, near);
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

        var hash = userId.ToByteArray().Aggregate(offsetBasis, (current, b) => (current ^ b) * prime);

        hash = shelfKey.Aggregate(hash, (current, c) => (current ^ (byte)c) * prime);

        hash = BitConverter.GetBytes(now.UtcDateTime.Date.Ticks).Aggregate(hash, (current, b) => (current ^ b) * prime);

        return (int)(hash & int.MaxValue);
    }
}
