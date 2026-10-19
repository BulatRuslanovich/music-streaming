// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations.Home;

public static class ShelfKeys
{
    public const string ForYou = "forYou";
    public const string BecauseYouListened = "becauseYouListened";
    public const string Discover = "discover";
    public const string ArtistsForYou = "artistsForYou";

    public const string MixPool = "mixPool";

    // Артисты для полки «Потому что вы слушали» по очереди: каждый день первым идёт другой из любимых,
    // а не всегда главный. Совсем слабые (меньше MinArtistShare от главного) на полку не выходят.
    public static IReadOnlyList<Guid> BecauseYouListenedArtists(UserRecommendationContext context)
    {
        const double MinArtistShare = 0.25;

        var scores = context.Ranking.ArtistScores;
        var top = context.TopArtistIds;
        if (top.Count == 0)
            return [];

        var strongest = scores.GetValueOrDefault(top[0]);
        var eligible = top.Where(id => scores.GetValueOrDefault(id) >= strongest * MinArtistShare).ToList();

        var start = Explorer.SeedFor(context.UserId, BecauseYouListened, context.Ranking.Now) % eligible.Count;

        return [.. eligible.Skip(start), .. eligible.Take(start)];
    }

    public static string BaseOf(string shelfKey)
    {
        var separator = shelfKey.IndexOf(':');
        return separator < 0 ? shelfKey : shelfKey[..separator];
    }
}
