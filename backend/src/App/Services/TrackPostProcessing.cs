// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Services.Integrations;
using Domain.Common;
using Domain.Entities;

namespace App.Services;

public class TrackPostProcessing(
    TranscodeQueue transcodeQueue,
    LibraryEnrichmentQueue enrichmentQueue)
{
    public void Schedule(Track track, IReadOnlyList<Guid> newArtistIds)
    {
        if (track.Codec is "alac")
            transcodeQueue.TryEnqueueUrgent(new TranscodeRequest(track.ContentHash, track.FilePath, AudioQuality.Normal));

        foreach (var request in TranscodeWarmup.For(track.ContentHash, track.FilePath, track.Codec, track.BitrateKbps))
            transcodeQueue.TryEnqueueWarmup(request);

        enrichmentQueue.TryEnqueue(new LibraryEnrichmentRequest(track.Id, newArtistIds));
    }
}
