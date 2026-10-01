// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace App.Recommendations.Embeddings;

public readonly record struct TrackVectorMeta(
    Guid TrackId,
    Guid ArtistId,
    string ContentHash,
    string SongKey,
    DateTimeOffset CreatedAt,
    int ClusterId,
    int SkippedEarlyCount);

public readonly record struct ScoredRow(int Row, Guid TrackId, float Score);

public interface IVectorSimilarity
{
    double Between(int rowA, int rowB);
}

public sealed class EmbeddingSnapshot : IVectorSimilarity
{
    private const int ParallelThreshold = 1500;

    private readonly float[] _matrix;
    private readonly TrackVectorMeta[] _meta;
    private readonly Dictionary<Guid, int> _rowByTrack;
    private readonly Dictionary<string, List<Guid>> _byContentHash;
    private readonly Dictionary<string, List<Guid>> _bySongKey;

    public int Count { get; }
    public int Dimension { get; }
    public DateTimeOffset BuiltAt { get; }

    public static EmbeddingSnapshot Empty { get; } = new();

    private EmbeddingSnapshot()
    {
        _matrix = [];
        _meta = [];
        _rowByTrack = [];
        _byContentHash = [];
        _bySongKey = [];
        BuiltAt = DateTimeOffset.MinValue;
    }

    public EmbeddingSnapshot(
        float[] matrix,
        TrackVectorMeta[] meta,
        int dimension,
        DateTimeOffset builtAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        if (matrix.Length != meta.Length * dimension)
            throw new ArgumentException(
                $"Matrix holds {matrix.Length} floats, expected {meta.Length} x {dimension}.", nameof(matrix));

        _matrix = matrix;
        _meta = meta;
        Count = meta.Length;
        Dimension = dimension;
        BuiltAt = builtAt;

        _rowByTrack = new Dictionary<Guid, int>(Count);
        _byContentHash = [];
        _bySongKey = [];

        for (var row = 0; row < Count; row++)
        {
            var item = meta[row];
            _rowByTrack[item.TrackId] = row;

            if (!string.IsNullOrEmpty(item.ContentHash))
                Append(_byContentHash, item.ContentHash, item.TrackId);

            if (!string.IsNullOrEmpty(item.SongKey))
                Append(_bySongKey, item.SongKey, item.TrackId);
        }
    }

    public bool IsEmpty => Count == 0;

    public int RowOf(Guid trackId) => _rowByTrack.GetValueOrDefault(trackId, -1);

    public TrackVectorMeta MetaAt(int row) => _meta[row];

    public ReadOnlySpan<float> Vector(int row) => _matrix.AsSpan(row * Dimension, Dimension);

    public double Between(int rowA, int rowB) =>
        rowA < 0 || rowB < 0 || rowA >= Count || rowB >= Count
            ? 0
            : TensorPrimitives.Dot(Vector(rowA), Vector(rowB));

    public float[] SimilaritiesTo(ReadOnlySpan<float> query)
    {
        var result = new float[Count];

        if (query.Length != Dimension)
            return result;

        if (Count < ParallelThreshold || Environment.ProcessorCount < 2)
        {
            for (var row = 0; row < Count; row++)
                result[row] = TensorPrimitives.Dot(query, Vector(row));

            return result;
        }

        var matrix = _matrix;
        var dimension = Dimension;
        var queryCopy = query.ToArray();

        Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, Count), range =>
        {
            for (var row = range.Item1; row < range.Item2; row++)
                result[row] = TensorPrimitives.Dot(queryCopy, matrix.AsSpan(row * dimension, dimension));
        });

        return result;
    }

    public IReadOnlyList<ScoredRow> TopK(ReadOnlySpan<float> query, int k, IReadOnlySet<Guid>? exclude = null)
    {
        if (k <= 0 || Count == 0 || query.Length != Dimension)
            return [];

        var scores = SimilaritiesTo(query);
        var heap = new PriorityQueue<int, (float Score, int NegativeRow)>(k);

        for (var row = 0; row < Count; row++)
        {
            if (exclude is not null && exclude.Contains(_meta[row].TrackId))
                continue;

            var priority = (scores[row], -row);

            if (heap.Count < k)
            {
                heap.Enqueue(row, priority);
                continue;
            }

            heap.EnqueueDequeue(row, priority);
        }

        var result = new List<ScoredRow>(heap.Count);
        while (heap.TryDequeue(out var row, out var priority))
            result.Add(new ScoredRow(row, _meta[row].TrackId, priority.Score));

        result.Reverse();
        return result;
    }

    public IReadOnlyList<Guid> CloneIds(Guid trackId)
    {
        var row = RowOf(trackId);
        if (row < 0)
            return [trackId];

        var item = _meta[row];
        var family = new HashSet<Guid> { trackId };

        if (!string.IsNullOrEmpty(item.ContentHash) && _byContentHash.TryGetValue(item.ContentHash, out var byHash))
            family.UnionWith(byHash);

        if (!string.IsNullOrEmpty(item.SongKey) && _bySongKey.TryGetValue(item.SongKey, out var bySong))
            family.UnionWith(bySong);

        return [.. family];
    }

    public double SeedSimilarity(int row, IReadOnlyList<(int Row, double Weight)> seeds)
    {
        if (row < 0 || seeds.Count == 0)
            return 0;

        var best = 0.0;
        foreach (var (seedRow, weight) in seeds)
        {
            if (seedRow < 0 || seedRow == row)
                continue;

            best = Math.Max(best, weight * Between(row, seedRow));
        }

        return best;
    }

    public static float[] ToPercentiles(ReadOnlySpan<float> similarities)
    {
        var count = similarities.Length;
        if (count == 0)
            return [];

        if (count == 1)
            return [1f];

        var order = new int[count];
        for (var i = 0; i < count; i++)
            order[i] = i;

        var copy = similarities.ToArray();
        Array.Sort(order, (a, b) => copy[a].CompareTo(copy[b]));

        var result = new float[count];
        for (var rank = 0; rank < count; rank++)
            result[order[rank]] = rank / (float)(count - 1);

        return result;
    }

    private static void Append(Dictionary<string, List<Guid>> map, string key, Guid trackId)
    {
        if (!map.TryGetValue(key, out var ids))
            map[key] = ids = [];

        ids.Add(trackId);
    }

    public static string SongKeyOf(string? artist, string? title)
    {
        var left = artist?.Trim();
        var right = title?.Trim();

        return string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)
            ? string.Empty
            : $"{left.ToLowerInvariant()}|{right.ToLowerInvariant()}";
    }
}
