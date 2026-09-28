// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests;

public class ImpressionQueueTests
{
    [Fact]
    public void A_batch_that_did_not_fit_is_not_counted_as_accepted()
    {
        var queue = new ImpressionQueue();
        var batch = new ImpressionBatch(Guid.CreateVersion7(), [], DateTimeOffset.UtcNow);

        var accepted = 0;
        while (queue.TryEnqueue(batch) && accepted < 100_000)
            accepted++;

        Assert.True(accepted < 100_000, "the queue never reported being full");
        Assert.Equal(accepted, queue.Accepted);
    }
}
