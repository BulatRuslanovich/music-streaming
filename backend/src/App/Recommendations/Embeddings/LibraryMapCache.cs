// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;

namespace App.Recommendations.Embeddings;

// Карта строится в фоне по снимку индекса и держится в памяти до следующего снимка. Пока новая
// строится, отдаётся прошлая: несколько новых треков не стоят того, чтобы ждать раскладку.
public sealed class LibraryMapCache(ILogger<LibraryMapCache> logger)
{
    private readonly Lock _gate = new();
    private EmbeddingSnapshot? _source;
    private Task<MapLayout>? _building;
    private MapLayout? _ready;

    public async Task<MapLayout> GetAsync(EmbeddingSnapshot snapshot, CancellationToken ct)
    {
        Task<MapLayout> building;
        MapLayout? stale;

        lock (_gate)
        {
            if (!ReferenceEquals(_source, snapshot) || _building is null)
            {
                var previous = _ready;
                _source = snapshot;
                _building = Task.Run(() => Build(snapshot, previous), CancellationToken.None);
            }

            building = _building;
            stale = _ready;
        }

        // Пустая прошлая карта (её строили, пока индекс ещё грузился) — не замена настоящей.
        if (stale is { Count: > 0 } && !building.IsCompletedSuccessfully)
            return stale;

        return await building.WaitAsync(ct);
    }

    private MapLayout Build(EmbeddingSnapshot snapshot, MapLayout? previous)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var layout = LibraryMapLayout.Compute(snapshot, previous);

            lock (_gate)
            {
                if (ReferenceEquals(_source, snapshot))
                    _ready = layout;
            }

            logger.LogInformation(
                "Library map laid out: {Count} tracks in {Elapsed} ms",
                layout.Count,
                (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return layout;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library map layout failed");

            // Следующий запрос попробует заново, а не получит ту же ошибку до смены индекса.
            lock (_gate)
            {
                if (ReferenceEquals(_source, snapshot))
                    _building = null;
            }

            throw;
        }
    }
}
