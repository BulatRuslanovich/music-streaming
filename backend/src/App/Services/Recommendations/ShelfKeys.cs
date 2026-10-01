// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Services.Recommendations;

public static class ShelfKeys
{
    public const string ForYou = "forYou";
    public const string BecauseYouListened = "becauseYouListened";
    public const string Discover = "discover";
    public const string ArtistsForYou = "artistsForYou";

    public const string MixPool = "mixPool";

    public static string Seeded(string key, Guid seed) => $"{key}:{seed}";

    public static string BaseOf(string shelfKey)
    {
        var separator = shelfKey.IndexOf(':');
        return separator < 0 ? shelfKey : shelfKey[..separator];
    }
}
