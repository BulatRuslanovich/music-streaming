// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Recommendations.Embeddings;
using App.Services;

namespace UnitTests.Recommendations;

public class RecapAggregateTests
{
    private static readonly Guid TrackA = Guid.NewGuid();
    private static readonly Guid TrackB = Guid.NewGuid();
    private static readonly Guid ArtistX = Guid.NewGuid();
    private static readonly Guid ArtistY = Guid.NewGuid();
    private static readonly Guid Rock = Guid.NewGuid();

    private static readonly Dictionary<Guid, RecapTrackFacts> Facts = new()
    {
        [TrackA] = new RecapTrackFacts([ArtistX, ArtistY], Rock),
        [TrackB] = new RecapTrackFacts([ArtistY], null),
    };

    [Fact]
    public void A_short_attempt_adds_time_but_is_not_a_play()
    {
        var totals = Totals(Play(TrackA, 3, 9, 200), Play(TrackB, 4, 10, 12));

        Assert.Equal(212, totals.Seconds);
        Assert.Equal(1, totals.Plays);
        Assert.Equal(1, totals.DistinctTracks);
        Assert.DoesNotContain(totals.Tracks, item => item.Id == TrackB);
    }

    [Fact]
    public void Tracks_rank_by_plays_and_artists_by_time()
    {
        var totals = Totals(
            Play(TrackA, 1, 9, 60), Play(TrackA, 1, 10, 60),
            Play(TrackB, 2, 9, 400));

        Assert.Equal(TrackA, totals.Tracks[0].Id);
        Assert.Equal(2, totals.Tracks[0].Plays);
        // У ArtistY оба трека: 520 секунд против 120 у ArtistX.
        Assert.Equal(ArtistY, totals.Artists[0].Id);
        Assert.Equal(520, totals.Artists[0].Seconds);
    }

    [Fact]
    public void Days_and_hours_follow_local_time()
    {
        var totals = Totals(Play(TrackA, 30, 23, 100), Play(TrackA, 1, 0, 50));

        Assert.Equal(30, totals.DaySeconds.Length);
        Assert.Equal(100, totals.DaySeconds[29]);
        Assert.Equal(50, totals.DaySeconds[0]);
        Assert.Equal(100, totals.HourSeconds[23]);
    }

    [Fact]
    public void Genre_shares_count_only_tracks_with_a_genre()
    {
        var totals = Totals(Play(TrackA, 1, 9, 100), Play(TrackB, 1, 9, 300));

        Assert.Equal((Rock, 1.0), Assert.Single(totals.Genres));
    }

    [Fact]
    public void Each_track_counts_towards_its_strongest_mood()
    {
        var snapshot = Library(TrackA, TrackB);
        IReadOnlyList<(string, float[])> moods = [("loud", [1f, 0.2f]), ("calm", [0.3f, 0.9f])];

        var shares = RecapAggregate.MoodShares([Play(TrackA, 1, 9, 300), Play(TrackB, 1, 9, 100)], snapshot, moods);

        Assert.Equal([("loud", 0.75), ("calm", 0.25)], shares);
    }

    [Fact]
    public void The_sound_of_the_month_leans_to_what_was_played_longest()
    {
        var snapshot = Library(TrackA, TrackB);

        Assert.Equal(TrackB, RecapAggregate.SoundOf([Play(TrackA, 1, 9, 30), Play(TrackB, 1, 9, 600)], snapshot));
        Assert.Null(RecapAggregate.SoundOf([Play(Guid.NewGuid(), 1, 9, 600)], snapshot));
    }

    private static RecapTotals Totals(params RecapPlay[] plays) =>
        RecapAggregate.Totals(plays, Facts, 2026, 9, top: 5);

    private static RecapPlay Play(Guid trackId, int day, int hour, int seconds) =>
        new(trackId, new DateTime(2026, 9, day, hour, 0, 0), seconds);

    private static EmbeddingSnapshot Library(Guid first, Guid second)
    {
        TrackVectorMeta Meta(Guid id) => new(id, Guid.NewGuid(), id.ToString(), id.ToString(), DateTimeOffset.UnixEpoch, 0);

        return new EmbeddingSnapshot([.. Vectors.Unit(1f, 0f), .. Vectors.Unit(0f, 1f)], [Meta(first), Meta(second)], 2);
    }
}
