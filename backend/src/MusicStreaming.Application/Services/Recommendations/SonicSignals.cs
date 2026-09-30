// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Services.Recommendations;

public sealed class SonicSignals
{
    private readonly EmbeddingSnapshot _snapshot;
    private readonly float[] _tastePercentiles;
    private readonly IReadOnlyList<(int Row, double Weight)> _seeds;

    public static SonicSignals None { get; } = new(EmbeddingSnapshot.Empty, [], []);

    private SonicSignals(
        EmbeddingSnapshot snapshot,
        float[] tastePercentiles,
        IReadOnlyList<(int Row, double Weight)> seeds)
    {
        _snapshot = snapshot;
        _tastePercentiles = tastePercentiles;
        _seeds = seeds;
    }

    public static SonicSignals Build(
        EmbeddingSnapshot snapshot,
        ReadOnlySpan<float> tasteQuery,
        IReadOnlyList<RecommendationSeed> seeds)
    {
        if (snapshot.IsEmpty)
            return None;

        var percentiles = tasteQuery.IsEmpty
            ? []
            : EmbeddingSnapshot.ToPercentiles(snapshot.SimilaritiesTo(tasteQuery));

        var seedRows = new List<(int Row, double Weight)>(seeds.Count);
        foreach (var seed in seeds)
        {
            var row = snapshot.RowOf(seed.TrackId);
            if (row >= 0)
                seedRows.Add((row, seed.Weight));
        }

        return new SonicSignals(snapshot, percentiles, seedRows);
    }

    public TrackSonicSignals For(Guid trackId)
    {
        var row = _snapshot.RowOf(trackId);
        if (row < 0)
            return TrackSonicSignals.None;

        return new TrackSonicSignals(
            row,
            _tastePercentiles.Length == 0 ? null : _tastePercentiles[row],
            _seeds.Count == 0 ? null : _snapshot.SeedSimilarity(row, _seeds));
    }
}

public readonly record struct TrackSonicSignals(int Row, double? TasteFit, double? SeedSimilarity)
{
    public static TrackSonicSignals None { get; } = new(-1, null, null);
}
