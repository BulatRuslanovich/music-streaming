// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services;
using Xunit;

namespace MusicStreaming.UnitTests;

public class AudioAnalysisQueueTests
{
    [Fact]
    public void A_track_is_queued_only_once_until_processing_finishes()
    {
        var queue = new AudioAnalysisQueue();
        var trackId = Guid.CreateVersion7();

        Assert.True(queue.TryEnqueue(trackId));
        Assert.False(queue.TryEnqueue(trackId));

        queue.MarkFinished(trackId);

        Assert.True(queue.TryEnqueue(trackId));
    }

    [Fact]
    public async Task A_track_refused_by_a_full_queue_can_be_queued_once_there_is_room()
    {
        var queue = new AudioAnalysisQueue();

        var accepted = 0;
        while (queue.TryEnqueue(Guid.CreateVersion7()) && accepted < 100_000)
            accepted++;

        Assert.True(accepted < 100_000, "the queue never reported being full");

        var refused = Guid.CreateVersion7();
        Assert.False(queue.TryEnqueue(refused));

        await using var reader = queue.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await reader.MoveNextAsync());

        Assert.True(queue.TryEnqueue(refused));
    }

    [Fact]
    public async Task Tracks_are_read_in_the_order_they_were_queued()
    {
        var queue = new AudioAnalysisQueue();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));

        await using var reader = queue.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(first, reader.Current);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(second, reader.Current);
    }
}
