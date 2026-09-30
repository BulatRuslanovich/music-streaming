// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using static MusicStreaming.Application.Recommendations.RecommendationTuning;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Recommendations.Queue;

public record QueueRequest(
    int CurrentRow,
    float[] Taste,
    IReadOnlySet<Guid> Exclude,
    double ExploreRatio,
    IReadOnlyDictionary<Guid, double> TransitionsFrom,
    int Size,
    DateTimeOffset Now,
    int Seed);

public record QueueItem(
    Guid TrackId,
    int Row,
    double Score,
    double CosineTaste,
    bool Explore,
    bool NewBoost);

public static class QueueBuilder
{
    private const double TasteWeight = 0.55;

    private const double CurrentWeight = 0.35;

    private const double TransitionWeight = 0.20;

    private const double SameClusterBonus = 0.03;

    private const double NewBoostBeta = 0.25;

    private const double NewBoostTauDays = 14.0;

    private const double NewTrackDays = 14.0;

    private const int NewBoostSkipGate = 3;

    private const double NewShareCap = 0.3;

    public static IReadOnlyList<QueueItem> Build(
        EmbeddingSnapshot snapshot,
        QueueRequest request)
    {
        if (snapshot.IsEmpty || request.Size <= 0)
            return [];

        var random = new Random(request.Seed);
        var size = request.Size;

        var tasteSimilarities = request.Taste.Length == snapshot.Dimension
            ? snapshot.SimilaritiesTo(request.Taste)
            : new float[snapshot.Count];

        var currentSimilarities = request.CurrentRow >= 0
            ? snapshot.SimilaritiesTo(snapshot.Vector(request.CurrentRow))
            : tasteSimilarities;

        var current = request.CurrentRow >= 0 ? snapshot.MetaAt(request.CurrentRow) : default;
        var maxTransition = request.TransitionsFrom.Count == 0 ? 0 : request.TransitionsFrom.Values.Max();

        var allowed = new List<int>(snapshot.Count);
        for (var row = 0; row < snapshot.Count; row++)
        {
            if (row == request.CurrentRow)
                continue;

            var meta = snapshot.MetaAt(row);
            if (request.Exclude.Contains(meta.TrackId))
                continue;

            if (!string.IsNullOrEmpty(current.ContentHash) && meta.ContentHash == current.ContentHash)
                continue;

            allowed.Add(row);
        }

        if (allowed.Count == 0)
            return [];

        var threshold = VectorMath.Quantile([.. allowed.Select(row => tasteSimilarities[row])], Exploration.FarQuantile);

        var near = new List<Candidate>(allowed.Count);
        var far = new List<Candidate>();

        foreach (var row in allowed)
        {
            var meta = snapshot.MetaAt(row);
            var taste = tasteSimilarities[row];
            var toCurrent = currentSimilarities[row];

            var ageDays = Math.Max(0, (request.Now - meta.CreatedAt).TotalDays);
            var boost = meta.SkippedEarlyCount >= NewBoostSkipGate || meta.CreatedAt == default || ageDays > NewTrackDays
                ? 0
                : NewBoostBeta * Math.Exp(-ageDays / NewBoostTauDays);

            var transition = maxTransition > 0 && request.TransitionsFrom.TryGetValue(meta.TrackId, out var weight) && weight > 0
                ? TransitionWeight * (Math.Log(1 + weight) / Math.Log(1 + maxTransition))
                : 0;

            var score = TasteWeight * taste + CurrentWeight * toCurrent + boost + transition;

            if (current.ClusterId >= 0 && meta.ClusterId == current.ClusterId)
                score += SameClusterBonus;

            near.Add(new Candidate(row, meta, score, taste, boost, Explore: false));

            if (taste <= threshold)
            {
                var farScore = -taste + random.NextDouble() * Exploration.FarJitter;

                far.Add(new Candidate(row, meta, farScore, taste, boost, Explore: true));
            }
        }

        near.Sort(static (left, right) => right.Score.CompareTo(left.Score));
        far.Sort(static (left, right) => right.Score.CompareTo(left.Score));

        var farWanted = (int)Math.Round(size * request.ExploreRatio, MidpointRounding.AwayFromZero);

        if (size >= 3 && request.ExploreRatio > 0 && farWanted < 1)
            farWanted = 1;

        farWanted = Math.Min(farWanted, size * 2 / 3);

        var state = new Selection((int)Math.Ceiling(size * NewShareCap), Diversity.MaxPerArtist, current);

        var nearPicked = state.Take(near, size - farWanted, explore: false);
        var farPicked = state.Take(far, farWanted, explore: true);

        if (nearPicked.Count + farPicked.Count < size)
            nearPicked.AddRange(state.TakeRelaxed(near, size - nearPicked.Count - farPicked.Count));

        return Interleave(nearPicked, farPicked);
    }

    private static List<QueueItem> Interleave(List<Candidate> near, List<Candidate> far)
    {
        if (far.Count == 0)
            return [.. near.Select(item => item.ToItem())];

        if (near.Count == 0)
            return [.. far.Select(item => item.ToItem())];

        var gap = Math.Max(2, near.Count / far.Count);
        var result = new List<QueueItem>(near.Count + far.Count);
        var nextFar = 0;
        var sinceFar = 0;

        foreach (var candidate in near)
        {
            result.Add(candidate.ToItem());
            sinceFar++;

            if (nextFar < far.Count && sinceFar >= gap)
            {
                result.Add(far[nextFar++].ToItem());
                sinceFar = 0;
            }
        }

        while (nextFar < far.Count)
            result.Add(far[nextFar++].ToItem());

        return result;
    }

    private readonly record struct Candidate(
        int Row,
        TrackVectorMeta Meta,
        double Score,
        double Taste,
        double Boost,
        bool Explore)
    {
        public bool IsNew => Boost > 0.01;

        public QueueItem ToItem() => new(Meta.TrackId, Row, Score, Taste, Explore, IsNew);
    }

    private sealed class Selection(int newCap, int maxPerArtist, TrackVectorMeta current)
    {
        private readonly HashSet<int> _used = [];
        private readonly Dictionary<Guid, int> _artists = [];
        private readonly HashSet<string> _hashes = string.IsNullOrEmpty(current.ContentHash) ? [] : [current.ContentHash];
        private readonly HashSet<string> _songs = string.IsNullOrEmpty(current.SongKey) ? [] : [current.SongKey];
        private int _new;

        public List<Candidate> Take(List<Candidate> source, int wanted, bool explore)
        {
            var taken = new List<Candidate>(Math.Max(0, wanted));

            foreach (var candidate in source)
            {
                if (taken.Count >= wanted)
                    break;

                if (_used.Contains(candidate.Row)
                    || _artists.GetValueOrDefault(candidate.Meta.ArtistId) >= maxPerArtist
                    || IsDuplicate(candidate)
                    || (candidate.IsNew && _new >= newCap))
                    continue;

                Accept(candidate);
                taken.Add(candidate with { Explore = explore });
            }

            return taken;
        }

        public List<Candidate> TakeRelaxed(List<Candidate> source, int wanted)
        {
            var taken = new List<Candidate>(Math.Max(0, wanted));

            foreach (var candidate in source)
            {
                if (taken.Count >= wanted)
                    break;

                if (_used.Contains(candidate.Row) || IsDuplicate(candidate))
                    continue;

                Accept(candidate);

                taken.Add(candidate with { Boost = 0 });
            }

            return taken;
        }

        private bool IsDuplicate(Candidate candidate)
        {
            if (!string.IsNullOrEmpty(candidate.Meta.ContentHash) && _hashes.Contains(candidate.Meta.ContentHash))
                return true;

            return !string.IsNullOrEmpty(candidate.Meta.SongKey) && _songs.Contains(candidate.Meta.SongKey);
        }

        private void Accept(Candidate candidate)
        {
            _used.Add(candidate.Row);
            _artists[candidate.Meta.ArtistId] = _artists.GetValueOrDefault(candidate.Meta.ArtistId) + 1;

            if (!string.IsNullOrEmpty(candidate.Meta.ContentHash))
                _hashes.Add(candidate.Meta.ContentHash);

            if (!string.IsNullOrEmpty(candidate.Meta.SongKey))
                _songs.Add(candidate.Meta.SongKey);

            if (candidate.IsNew)
                _new++;
        }
    }
}
