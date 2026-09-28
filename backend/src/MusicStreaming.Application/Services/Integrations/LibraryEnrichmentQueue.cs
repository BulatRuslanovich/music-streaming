// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services.Integrations;

public record LibraryEnrichmentRequest(Guid TrackId, IReadOnlyList<Guid> NewArtistIds);

/// <summary>Freshly uploaded tracks waiting for artist images and lyrics from external services.</summary>
public class LibraryEnrichmentQueue
{
    // Wait: при переполнении TryWrite честно отвечает false, а не выбрасывает заявку молча.
    private readonly Channel<LibraryEnrichmentRequest> _channel =
        Channel.CreateBounded<LibraryEnrichmentRequest>(
            new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    // Треки, которые стоят в очереди или обрабатываются прямо сейчас, — чтобы один трек не
    // обогащался дважды. Ключ держится до MarkFinished.
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    /// <summary>Queues a track. False when the track is already queued or the queue is full.</summary>
    public bool TryEnqueue(LibraryEnrichmentRequest request)
    {
        if (!_queued.TryAdd(request.TrackId, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _queued.TryRemove(request.TrackId, out _);
        return false;
    }

    public IAsyncEnumerable<LibraryEnrichmentRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>The worker is done with the track, so it may be queued again.</summary>
    public void MarkFinished(LibraryEnrichmentRequest request) => _queued.TryRemove(request.TrackId, out _);
}
