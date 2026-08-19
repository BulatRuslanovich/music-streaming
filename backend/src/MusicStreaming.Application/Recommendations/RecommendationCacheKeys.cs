// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static class RecommendationCacheKeys
{
    public static string Shelves(Guid userId) => $"recommendations:{userId}";

    public const string GenreShare = "recommendations:genre-share";
}
