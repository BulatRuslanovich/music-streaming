// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Abstractions;

public interface IImageProcessor
{
    Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
        Stream source, IReadOnlyList<int> edges, CancellationToken ct = default);
}

public record ResizedImage(int Edge, byte[] Content);
