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
}
