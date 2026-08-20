// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Services.Integrations;

public record LibraryEnrichmentRequest(Guid TrackId, IReadOnlyList<Guid> NewArtistIds);

public class LibraryEnrichmentQueue(IOptions<LibraryEnrichmentOptions> options)
{
    private const int Capacity = 2048;

    private readonly Channel<LibraryEnrichmentRequest> _channel =
        Channel.CreateBounded<LibraryEnrichmentRequest>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<Guid, byte> _pending = [];

    public bool TryEnqueue(LibraryEnrichmentRequest request)
    {
        if (!options.Value.Enabled || !_pending.TryAdd(request.TrackId, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _pending.TryRemove(request.TrackId, out _);
        return false;
    }

    public IAsyncEnumerable<LibraryEnrichmentRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void MarkFinished(LibraryEnrichmentRequest request) =>
        _pending.TryRemove(request.TrackId, out _);
}
