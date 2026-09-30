// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class TrackPostProcessing(
    TranscodeQueue transcodeQueue,
    LibraryEnrichmentQueue enrichmentQueue)
{
    public void Schedule(Track track, IReadOnlyList<Guid> newArtistIds)
    {
        if (track.Codec is "alac")
            transcodeQueue.TryEnqueueUrgent(new TranscodeRequest(track.ContentHash, track.FilePath, AudioQuality.Normal));

        foreach (var request in TranscodeWarmup.For(track.ContentHash, track.FilePath))
            transcodeQueue.TryEnqueueWarmup(request);

        enrichmentQueue.TryEnqueue(new LibraryEnrichmentRequest(track.Id, newArtistIds));
    }
}
