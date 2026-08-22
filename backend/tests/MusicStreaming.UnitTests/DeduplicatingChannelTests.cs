// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;
using MusicStreaming.Application.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class DeduplicatingChannelTests
{
    [Fact]
    public void An_item_can_be_queued_again_only_after_processing_finishes()
    {
        var channel = Channel(capacity: 2);

        Assert.True(channel.TryEnqueue("track"));
        Assert.False(channel.TryEnqueue("track"));

        channel.MarkFinished("track");

        Assert.True(channel.TryEnqueue("track"));
    }

    [Fact]
    public async Task A_rejected_item_does_not_remain_pending_when_the_channel_is_full()
    {
        var channel = Channel(capacity: 1);

        Assert.True(channel.TryEnqueue("first"));
        Assert.False(channel.TryEnqueue("second"));

        await using var reader = channel.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("first", reader.Current);
        Assert.True(channel.TryEnqueue("second"));
    }

    [Fact]
    public async Task Items_are_read_in_the_order_they_were_queued()
    {
        var channel = Channel(capacity: 2);
        Assert.True(channel.TryEnqueue("first"));
        Assert.True(channel.TryEnqueue("second"));

        await using var reader = channel.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("first", reader.Current);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("second", reader.Current);
    }

    private static DeduplicatingChannel<string, string> Channel(int capacity) =>
        new(capacity, BoundedChannelFullMode.Wait, item => item, StringComparer.Ordinal);
}
