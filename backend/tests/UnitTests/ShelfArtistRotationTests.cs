// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using Domain.Entities.Recommendations;
using App.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;

namespace UnitTests.Recommendations;

public class ShelfArtistRotationTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_listener_without_favourites_gets_no_artist_shelf() =>
        Assert.Empty(ShelfKeys.BecauseYouListenedArtists(Context(Today)));

    [Fact]
    public void Over_a_few_days_the_shelf_is_named_after_more_than_one_favourite()
    {
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var leads = Enumerable.Range(0, 14)
            .Select(day => ShelfKeys.BecauseYouListenedArtists(
                Context(Today.AddDays(day), (first, 10), (second, 8), (third, 6)))[0])
            .ToHashSet();

        Assert.True(leads.Count > 1, "The same artist led the shelf for two weeks");
    }

    [Fact]
    public void Within_a_day_the_choice_holds_still()
    {
        (Guid, double)[] artists = [(Guid.NewGuid(), 10), (Guid.NewGuid(), 8), (Guid.NewGuid(), 6)];
        var morning = new DateTimeOffset(Today.UtcDateTime.Date, TimeSpan.Zero).AddHours(1);

        Assert.Equal(
            ShelfKeys.BecauseYouListenedArtists(Context(morning, artists)),
            ShelfKeys.BecauseYouListenedArtists(Context(morning.AddHours(20), artists)));
    }

    [Fact]
    public void A_barely_heard_artist_never_names_the_shelf()
    {
        var (loved, faint) = (Guid.NewGuid(), Guid.NewGuid());

        foreach (var day in Enumerable.Range(0, 14))
        {
            var order = ShelfKeys.BecauseYouListenedArtists(Context(Today.AddDays(day), (loved, 10), (faint, 1)));

            Assert.Equal([loved], order);
        }
    }

    private static UserRecommendationContext Context(DateTimeOffset now, params (Guid Id, double Score)[] artists) =>
        new(
            UserId,
            ProfileMaturity.Mature,
            [.. artists.OrderByDescending(artist => artist.Score).Select(artist => artist.Id)],
            artists.ToDictionary(artist => artist.Id, artist => $"Artist {artist.Score}"),
            new RankingContext(
                artists.ToDictionary(artist => artist.Id, artist => artist.Score),
                new Dictionary<Guid, double>(),
                new Dictionary<Guid, TrackHistory>(),
                now),
            [],
            TasteModel.Empty);

    private static readonly Guid UserId = Guid.NewGuid();
}
