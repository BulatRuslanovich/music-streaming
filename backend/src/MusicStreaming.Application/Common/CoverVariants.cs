// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public enum CoverSize
{
    Full,
    Thumb,
}

public static class CoverVariants
{
    public const int FullEdge = 640;
    public const int ThumbEdge = 256;

    public static readonly IReadOnlyList<int> Edges = [FullEdge, ThumbEdge];
}
