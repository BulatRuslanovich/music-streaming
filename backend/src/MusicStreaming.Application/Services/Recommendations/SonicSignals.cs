// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Сигналы из пространства эмбеддингов для одной генерации: насколько трек близок к вкусу
/// слушателя и насколько он похож на то, что тот недавно слушал.
/// <para>
/// Оба считаются одним проходом по матрице в памяти, поэтому обращений к базе не добавляется.
/// Пустой экземпляр (нет индекса или нет вектора вкуса) отвечает <c>null</c> на всё, и скоринг
/// просто перераспределяет вес этих термов на остальные.
/// </para>
/// </summary>
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

        // Перцентиль, а не сырой косинус: он сравним между библиотеками и укладывается
        // в тот же диапазон 0..1, что и остальные термы скоринга.
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

    public int RowOf(Guid trackId) => _snapshot.RowOf(trackId);

    /// <summary>Перцентиль близости к вкусу, 0..1. null — у трека нет эмбеддинга или нет вкуса.</summary>
    public double? TasteFit(Guid trackId)
    {
        if (_tastePercentiles.Length == 0)
            return null;

        var row = _snapshot.RowOf(trackId);
        return row < 0 ? null : _tastePercentiles[row];
    }

    /// <summary>Лучший взвешенный косинус к сидам. null — у трека нет эмбеддинга или нет сидов.</summary>
    public double? SeedSimilarity(Guid trackId)
    {
        if (_seeds.Count == 0)
            return null;

        var row = _snapshot.RowOf(trackId);
        return row < 0 ? null : _snapshot.SeedSimilarity(row, _seeds);
    }
}
