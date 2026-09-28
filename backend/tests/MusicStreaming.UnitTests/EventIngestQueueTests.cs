// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests;

public class EventIngestQueueTests
{
    [Fact]
    public void A_full_queue_rejects_the_event_instead_of_dropping_it_silently()
    {
        var queue = new EventIngestQueue();

        var accepted = 0;
        while (queue.TryEnqueue(new PlaybackEvent()) && accepted < 100_000)
            accepted++;

        Assert.True(accepted < 100_000, "the queue never reported being full");
    }
}
