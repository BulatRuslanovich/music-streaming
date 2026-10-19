// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using App.Recommendations.Embeddings;

namespace UnitTests;

public class LibraryMapCacheTests
{
    // Карту спросили, пока индекс ещё грузился: пустая раскладка не должна выдаваться за прошлую,
    // когда индекс уже пришёл и настоящая строится.
    [Fact]
    public async Task An_empty_map_is_never_served_in_place_of_the_first_real_one()
    {
        var cache = new LibraryMapCache(NullLogger<LibraryMapCache>.Instance);

        var empty = await cache.GetAsync(EmbeddingSnapshot.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(0, empty.Count);

        var layout = await cache.GetAsync(Library(12), TestContext.Current.CancellationToken);

        Assert.Equal(12, layout.Count);
    }

    private static EmbeddingSnapshot Library(int count)
    {
        const int dimension = 8;
        var random = new Random(5);
        var rows = Enumerable.Range(0, count)
            .Select(_ => Vectors.Unit([.. Enumerable.Range(0, dimension).Select(_ => (float)random.NextDouble() - 0.5f)]))
            .ToArray();

        return new EmbeddingSnapshot(
            [.. rows.SelectMany(row => row)],
            [.. rows.Select(_ => new TrackVectorMeta(Guid.NewGuid(), Guid.Empty, "", "", DateTimeOffset.UnixEpoch, 0))],
            dimension);
    }
}
