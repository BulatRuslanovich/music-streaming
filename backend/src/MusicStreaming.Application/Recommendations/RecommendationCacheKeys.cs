// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Все ключи памяти в одном месте: кэш сбрасывает не тот код, что его пишет, и разошедшийся
/// литерал означал бы запись, которую никто не выкинет.
/// </summary>
public static class RecommendationCacheKeys
{
    public static string Shelves(Guid userId) => $"recommendations:{userId}";

    public static string TimeZone(Guid userId) => $"recommendations:timezone:{userId}";

    public static string LibraryStats(Guid userId) => $"library-stats:{userId}";

    public static string TrackHash(Guid trackId) => $"track-hash:{trackId}";

    public const string GenreShare = "recommendations:genre-share";
}
