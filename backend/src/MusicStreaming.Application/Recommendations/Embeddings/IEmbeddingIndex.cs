// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations.Embeddings;

/// <summary>
/// Матрица эмбеддингов библиотеки в оперативной памяти. Синглтон: 50k x 512 float — это 100 МБ,
/// которые не имеет смысла ни перечитывать на запрос, ни держать в нескольких копиях.
/// </summary>
public interface IEmbeddingIndex
{
    /// <summary>
    /// false, пока в индексе нет ни одной строки. Это та же ветка, по которой идёт совершенно
    /// новая библиотека, поэтому отсутствие модели не требует отдельных условий выше по стеку.
    /// </summary>
    bool IsReady { get; }

    /// <summary>Текущий снимок. Брать один раз на операцию и работать с ним.</summary>
    EmbeddingSnapshot Snapshot();

    /// <summary>Попросить пересобрать снимок при ближайшей возможности.</summary>
    void RequestReload();
}
