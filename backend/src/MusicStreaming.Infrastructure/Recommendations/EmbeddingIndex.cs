// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Infrastructure.Recommendations;

/// <summary>
/// Держатель текущего снимка матрицы эмбеддингов.
/// <para>
/// На пути чтения нет ни одной блокировки: читатель забирает ссылку на неизменяемый
/// <see cref="EmbeddingSnapshot"/> и работает с ним, а <see cref="EmbeddingIndexLoader"/> строит
/// новый снимок в фоне и подменяет ссылку целиком. Брать лок вокруг каждого скалярного
/// произведения означало бы сериализовать на нём весь внутренний цикл.
/// </para>
/// </summary>
public sealed class EmbeddingIndex : IEmbeddingIndex
{
    private volatile EmbeddingSnapshot _current = EmbeddingSnapshot.Empty;
    private volatile bool _reloadRequested;

    public bool IsReady => !_current.IsEmpty;

    public EmbeddingSnapshot Snapshot() => _current;

    public void RequestReload() => _reloadRequested = true;

    /// <summary>Снимает и возвращает флаг запроса на пересборку.</summary>
    internal bool ConsumeReloadRequest()
    {
        if (!_reloadRequested)
            return false;

        _reloadRequested = false;
        return true;
    }

    internal void Publish(EmbeddingSnapshot snapshot) => _current = snapshot;
}
