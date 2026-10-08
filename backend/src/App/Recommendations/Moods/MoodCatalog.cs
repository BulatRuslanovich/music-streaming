// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Text.Json;
using App.Recommendations.Embeddings;
using Infrastructure.Audio;

namespace App.Recommendations.Moods;

public sealed record Mood(string Key, float[] Vector);

// Как библиотека ложится на настроение. Ranks — доля библиотеки, которую трек обходит по счёту, в [0, 1]:
// для сортировки и «какое настроение у трека сильнее всего». Members — треки, которые настроению
// действительно подходят, — только из них собирается радио по настроению.
public sealed record MoodScores(float[] Ranks, bool[] Members)
{
    public int MemberCount { get; } = Members.Count(member => member);

    // Меньше подходящих треков — настроение не предлагается: радио из пяти треков — не радио.
    public bool Playable => MemberCount >= MoodCatalog.MinimumMembers;

    public static MoodScores Empty(int count) => new(new float[count], new bool[count]);
}

// Настроение — направление в пространстве CLAP: «на что похоже» минус «на что не похоже»
// (backend/scripts/export_clap_mood_vectors.py). Счёт трека — проекция на направление; положительный
// значит, что трек ближе к своей стороне, чем к противоположной. Это абсолютный признак, а не
// место в библиотеке: настроения, которого в библиотеке нет, не найдётся и в «верхних 30 %».
public sealed class MoodCatalog
{
    private const string ResourceName = "moods.json";

    // Трек подходит, если он ближе к своей стороне с запасом и заметно выше среднего по библиотеке.
    private const float MemberMargin = 0.02f;

    private const float MemberDeviation = 0.5f;

    public const int MinimumMembers = 12;

    private readonly ConditionalWeakTable<EmbeddingSnapshot, Dictionary<string, MoodScores>> _scores = new();

    public IReadOnlyList<Mood> All { get; }

    public MoodCatalog(ILogger<MoodCatalog> logger)
        : this(Load(logger))
    {
    }

    internal MoodCatalog(IReadOnlyList<Mood> moods) => All = moods;

    public Mood? Find(string key) => All.FirstOrDefault(mood => string.Equals(mood.Key, key, StringComparison.OrdinalIgnoreCase));

    public MoodScores ScoresIn(EmbeddingSnapshot snapshot, Mood mood)
    {
        if (snapshot.IsEmpty || mood.Vector.Length != snapshot.Dimension)
            return MoodScores.Empty(snapshot.Count);

        return _scores.GetValue(snapshot, Compute)[mood.Key];
    }

    // Ранг трека по настроению в [0, 1]: 1 — самый подходящий в библиотеке.
    public float[] RanksIn(EmbeddingSnapshot snapshot, Mood mood) => ScoresIn(snapshot, mood).Ranks;

    // Настроения, которые есть в этой библиотеке.
    public IEnumerable<Mood> AvailableIn(EmbeddingSnapshot snapshot) => All.Where(mood => ScoresIn(snapshot, mood).Playable);

    // Настроение трека — то, где его ранг выше всего; индекс в ranks.
    public static int Dominant(IReadOnlyList<float[]> ranks, int row)
    {
        var best = 0;
        for (var mood = 1; mood < ranks.Count; mood++)
            if (ranks[mood][row] > ranks[best][row])
                best = mood;

        return best;
    }

    private Dictionary<string, MoodScores> Compute(EmbeddingSnapshot snapshot) =>
        All.ToDictionary(mood => mood.Key, mood => Score(snapshot.SimilaritiesTo(mood.Vector)));

    internal static MoodScores Score(float[] raw)
    {
        var deviations = Standardize(raw.ToArray());
        var members = new bool[raw.Length];

        for (var row = 0; row < raw.Length; row++)
            members[row] = raw[row] > MemberMargin && deviations[row] >= MemberDeviation;

        return new MoodScores(VectorMath.PercentileRanks(raw), members);
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
