// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public interface IImageProcessor
{
    Task<byte[]> ToSquareWebpAsync(Stream source, int edge, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
        Stream source, IReadOnlyList<int> edges, CancellationToken cancellationToken = default);
}

public record ResizedImage(int Edge, byte[] Content);
