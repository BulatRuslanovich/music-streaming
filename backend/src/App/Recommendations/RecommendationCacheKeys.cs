// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations;

public static class RecommendationCacheKeys
{
    public static string Shelves(Guid userId) => $"recommendations:{userId}";

    public static string TrackHash(Guid trackId) => $"track-hash:{trackId}";

    public const string GenreShare = "recommendations:genre-share";
}
