// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

/// <summary>
/// Всё, что происходит с треком после коммита: перекодировка, разбор аудио, обогащение.
/// </summary>
/// <remarks>
/// Ни одна из этих очередей не влияет на исход загрузки — они лишь принимают заявку и отпускают
/// запрос. Держать их в сервисе загрузки значило носить четыре зависимости ради трёх строк,
/// выполняющихся уже после того, как ответ по сути готов.
/// </remarks>
public class TrackPostProcessing(
    TranscodeQueue transcodeQueue,
    AudioAnalysisQueue audioAnalysisQueue,
    LibraryEnrichmentQueue enrichmentQueue,
    IAudioTranscoder transcoder)
{
    public void Schedule(Track track, IReadOnlyList<Guid> newArtistIds)
    {
        PrepareUnplayableOriginal(track);
        PrepareAdaptiveStreams(track);

        audioAnalysisQueue.TryEnqueue(track.Id);
        enrichmentQueue.TryEnqueue(new LibraryEnrichmentRequest(track.Id, newArtistIds));
    }

    /// <summary>ALAC браузеры не играют: без перекодировки такой трек не зазвучит вообще.</summary>
    private void PrepareUnplayableOriginal(Track track)
    {
        if (track.Codec is not "alac" || !transcoder.IsAvailable)
            return;

        transcodeQueue.TryEnqueue(new TranscodeRequest(track.ContentHash, track.FilePath, AudioQuality.Normal));
    }

    private void PrepareAdaptiveStreams(Track track)
    {
        if (!transcoder.IsAvailable)
            return;

        foreach (var request in TranscodeWarmup.For(track.ContentHash, track.FilePath))
            transcodeQueue.TryEnqueueWarmup(request);
    }
}
