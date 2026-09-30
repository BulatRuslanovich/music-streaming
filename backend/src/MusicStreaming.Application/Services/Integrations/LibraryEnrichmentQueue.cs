// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services.Integrations;

public record LibraryEnrichmentRequest(Guid? TrackId, IReadOnlyList<Guid> ArtistIds);

public class LibraryEnrichmentQueue
{
    private readonly Channel<LibraryEnrichmentRequest> _channel =
        Channel.CreateBounded<LibraryEnrichmentRequest>(
            new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    public bool TryEnqueue(LibraryEnrichmentRequest request)
    {
        if (request.TrackId is { } trackId && !_queued.TryAdd(trackId, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        MarkFinished(request);
        return false;
    }

    public IAsyncEnumerable<LibraryEnrichmentRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void MarkFinished(LibraryEnrichmentRequest request)
    {
        if (request.TrackId is { } trackId)
            _queued.TryRemove(trackId, out _);
    }
}
