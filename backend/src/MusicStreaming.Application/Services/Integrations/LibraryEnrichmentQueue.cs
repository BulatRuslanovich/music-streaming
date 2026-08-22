// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Services.Integrations;

public record LibraryEnrichmentRequest(Guid TrackId, IReadOnlyList<Guid> NewArtistIds);

public class LibraryEnrichmentQueue(IOptions<LibraryEnrichmentOptions> options)
{
    private const int Capacity = 2048;

    private readonly DeduplicatingChannel<LibraryEnrichmentRequest, Guid> _queue =
        new(Capacity, BoundedChannelFullMode.Wait, request => request.TrackId);

    public bool TryEnqueue(LibraryEnrichmentRequest request)
    {
        return options.Value.Enabled && _queue.TryEnqueue(request);
    }

    public IAsyncEnumerable<LibraryEnrichmentRequest> ReadAllAsync(CancellationToken ct) =>
        _queue.ReadAllAsync(ct);

    public void MarkFinished(LibraryEnrichmentRequest request) => _queue.MarkFinished(request);
}
