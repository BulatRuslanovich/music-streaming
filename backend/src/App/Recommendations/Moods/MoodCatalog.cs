// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
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

    private readonly ConditionalWeakTable<EmbeddingSnapshot, Dictionary<string, float[]>> _ranks = new();

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

    private Dictionary<string, float[]> Compute(EmbeddingSnapshot snapshot)
    {
        var standardized = All.Select(mood => Standardize(snapshot.SimilaritiesTo(mood.Vector))).ToArray();

        var common = new float[snapshot.Count];
        foreach (var scores in standardized)
            TensorPrimitives.Add(common, scores, common);
        TensorPrimitives.Divide(common, standardized.Length, common);

        var result = new Dictionary<string, float[]>();
        for (var index = 0; index < All.Count; index++)
        {
            TensorPrimitives.Subtract(standardized[index], common, standardized[index]);
            result[All[index].Key] = VectorMath.PercentileRanks(standardized[index]);
        }

        return result;
    }

    private static float[] Standardize(float[] values)
    {
        TensorPrimitives.Subtract(values, TensorPrimitives.Sum(values) / values.Length, values);

        var deviation = TensorPrimitives.Norm(values) / MathF.Sqrt(values.Length);
        if (deviation < 1e-9f)
            Array.Clear(values);
        else
            TensorPrimitives.Divide(values, deviation, values);

        return values;
    }

    private static IReadOnlyList<Mood> Load(ILogger logger)
    {
        using var stream = typeof(MoodCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} is not embedded.");

        var file = JsonSerializer.Deserialize<MoodFile>(stream, JsonOptions)
                   ?? throw new InvalidOperationException($"{ResourceName} is empty.");

        // Векторы другой модели живут в другом пространстве: сравнивать их с эмбеддингами треков бессмысленно.
        if (file.ModelId != ClapAudioEmbedder.CheckpointId)
        {
            logger.LogWarning(
                "Mood vectors were computed for {MoodModel}, audio uses {AudioModel}; mood radio is off",
                file.ModelId, ClapAudioEmbedder.CheckpointId);
            return [];
        }

        return [.. file.Moods.Select(pair =>
        {
            VectorMath.NormalizeInPlace(pair.Value);
            return new Mood(pair.Key, pair.Value);
        })];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record MoodFile(string ModelId, Dictionary<string, float[]> Moods);
}
