// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Runtime.CompilerServices;
using System.Text.Json;
using App.Recommendations.Embeddings;
using Infrastructure.Audio;

namespace App.Recommendations.Moods;

public sealed record Mood(string Key, float[] Vector);

// Настроения — векторы текстовых описаний в пространстве CLAP (backend/scripts/export_clap_mood_vectors.py).
// Сырая близость трека к тексту мало что значит сама по себе: у каждого описания свой сдвиг, и есть
// треки, близкие к любому тексту. Поэтому близость нормируется по библиотеке (z по каждому
// настроению), из неё вычитается средняя по всем настроениям трека, и остаётся ранг — доля библиотеки,
// которую трек обходит по этому настроению.
public sealed class MoodCatalog
{
    private const string ResourceName = "moods.json";

    private readonly ConditionalWeakTable<EmbeddingSnapshot, IReadOnlyDictionary<string, float[]>> _ranks = new();

    public IReadOnlyList<Mood> All { get; }

    public MoodCatalog(ILogger<MoodCatalog> logger)
        : this(Load(logger))
    {
    }

    internal MoodCatalog(IReadOnlyList<Mood> moods) => All = moods;

    public Mood? Find(string key) => All.FirstOrDefault(mood => string.Equals(mood.Key, key, StringComparison.OrdinalIgnoreCase));

    // Ранг трека по настроению в [0, 1]: 1 — самый подходящий в библиотеке.
    public float[] RanksIn(EmbeddingSnapshot snapshot, Mood mood)
    {
        if (snapshot.IsEmpty || mood.Vector.Length != snapshot.Dimension)
            return new float[snapshot.Count];

        return _ranks.GetValue(snapshot, Compute)[mood.Key];
    }

    private IReadOnlyDictionary<string, float[]> Compute(EmbeddingSnapshot snapshot)
    {
        var count = snapshot.Count;
        var standardized = All.Select(mood => Standardize(snapshot.SimilaritiesTo(mood.Vector))).ToArray();

        var common = new float[count];
        foreach (var scores in standardized)
            for (var row = 0; row < count; row++)
                common[row] += scores[row] / standardized.Length;

        var result = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < All.Count; index++)
        {
            var contrast = new float[count];
            for (var row = 0; row < count; row++)
                contrast[row] = standardized[index][row] - common[row];

            result[All[index].Key] = Ranks(contrast);
        }

        return result;
    }

    private static float[] Standardize(float[] values)
    {
        var mean = values.Average();
        var deviation = Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / values.Length);
        var scale = deviation < 1e-9 ? 0 : 1 / deviation;

        return [.. values.Select(value => (float)((value - mean) * scale))];
    }

    private static float[] Ranks(float[] values)
    {
        var ranks = new float[values.Length];
        if (values.Length == 1)
        {
            ranks[0] = 1;
            return ranks;
        }

        var order = Enumerable.Range(0, values.Length).OrderBy(row => values[row]).ToArray();
        for (var position = 0; position < order.Length; position++)
            ranks[order[position]] = position / (float)(order.Length - 1);

        return ranks;
    }

    private static IReadOnlyList<Mood> Load(ILogger logger)
    {
        using var stream = typeof(MoodCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            logger.LogWarning("Mood vectors resource {Resource} is missing, mood radio is off", ResourceName);
            return [];
        }

        var file = JsonSerializer.Deserialize<MoodFile>(stream, JsonOptions)
                   ?? throw new InvalidOperationException($"{ResourceName} is empty.");

        // Векторы другой модели живут в другом пространстве: сравнивать их с эмбеддингами треков бессмысленно.
        if (file.ModelId != ClapAudioEmbedder.CheckpointId || file.Dimension != ClapAudioEmbedder.VectorDimension)
        {
            logger.LogWarning(
                "Mood vectors were computed for {MoodModel} ({MoodDimension}), audio uses {AudioModel}; mood radio is off",
                file.ModelId, file.Dimension, ClapAudioEmbedder.CheckpointId);
            return [];
        }

        return [.. file.Moods.Select(pair =>
        {
            var vector = pair.Value.Vector.ToArray();
            VectorMath.NormalizeInPlace(vector);
            return new Mood(pair.Key, vector);
        })];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record MoodFile(string ModelId, int Dimension, Dictionary<string, MoodEntry> Moods);

    private sealed record MoodEntry(IReadOnlyList<string> Prompts, float[] Vector);
}
