// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Security.Cryptography;
using System.Text;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Эмбеддер-дубль: раскладывает путь файла в псевдослучайный единичный вектор.
/// <para>
/// Нужен, чтобы весь рекомендательный контур — индекс, вектор вкуса, MMR, near/far, очередь —
/// можно было собирать и тестировать, не дожидаясь готовности ONNX-пути. Вектора здесь
/// <b>не несут смысла о звуке</b>: близость двух треков случайна. Единственное, что он
/// гарантирует, — детерминированность: один и тот же файл всегда даёт один и тот же вектор.
/// </para>
/// <para>
/// Включается настройкой <c>AudioEmbedding:Provider = "deterministic"</c> и нужен только для
/// локальной разработки: без модели на сотни мегабайт весь путь рекомендаций иначе не запустить.
/// В продакшене её значение — <c>clap</c>.
/// </para>
/// </summary>
public class DeterministicAudioEmbedder : IAudioEmbedder
{
    /// <summary>
    /// Длина вектора. Настройкой быть не может: она обязана совпадать с тем, что уже лежит в
    /// track_embeddings, — смена размерности обесценивает таблицу целиком.
    /// </summary>
    public const int DefaultDimension = 512;

    public bool IsAvailable => true;

    public string ModelId => "deterministic-v1";

    public string Strategy => "hash";

    public int Dimension => DefaultDimension;

    public Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken ct = default) =>
        Task.FromResult<AudioEmbedding?>(new AudioEmbedding(VectorFor(sourceAbsolutePath, Dimension), 1));

    /// <summary>Единичный вектор, однозначно определяемый ключом.</summary>
    public static float[] VectorFor(string key, int dimension)
    {
        var vector = new float[dimension];
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        // Счётчик подмешивается в хеш, чтобы получить столько байт, сколько нужно на всю
        // размерность, не повторяя один и тот же блок.
        for (var offset = 0; offset < dimension; offset += 16)
        {
            var block = SHA256.HashData([.. seed, .. BitConverter.GetBytes(offset)]);

            for (var i = 0; i < 16 && offset + i < dimension; i++)
            {
                // Два байта на компоненту, в симметричный диапазон вокруг нуля.
                var raw = BitConverter.ToUInt16(block, i * 2);
                vector[offset + i] = raw / 32768f - 1f;
            }
        }

        VectorMath.NormalizeInPlace(vector);
        return vector;
    }
}
