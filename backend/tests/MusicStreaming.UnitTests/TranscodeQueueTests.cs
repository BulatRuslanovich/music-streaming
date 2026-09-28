// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class TranscodeQueueTests
{
    private static TranscodeRequest Request(string hash = "hash") =>
        new(hash, "music/aa/bb/track.flac", AudioQuality.Low);

    [Fact]
    public void An_urgent_request_goes_through_while_the_same_rendition_waits_in_warmup()
    {
        var queue = new TranscodeQueue();

        Assert.True(queue.TryEnqueueWarmup(Request()));
        Assert.True(queue.TryEnqueueUrgent(Request()));
    }

    [Fact]
    public void A_full_lane_refuses_instead_of_dropping_silently()
    {
        var queue = new TranscodeQueue();

        var accepted = 0;
        while (queue.TryEnqueueUrgent(Request($"hash-{accepted}")) && accepted < 10_000)
            accepted++;

        Assert.True(accepted < 10_000, "the urgent lane never reported being full");
    }

    [Fact]
    public async Task A_refused_request_can_be_queued_once_the_lane_has_room()
    {
        var queue = new TranscodeQueue();

        var accepted = 0;
        while (queue.TryEnqueueUrgent(Request($"hash-{accepted}")) && accepted < 10_000)
            accepted++;

        var refused = Request($"hash-{accepted}");

        await using var reader = queue.ReadUrgentAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await reader.MoveNextAsync());

        Assert.True(queue.TryEnqueueUrgent(refused));
    }

    [Fact]
    public void The_same_rendition_takes_one_place_in_a_lane()
    {
        var queue = new TranscodeQueue();

        Assert.True(queue.TryEnqueueWarmup(Request()));
        Assert.False(queue.TryEnqueueWarmup(Request()));
    }
}
