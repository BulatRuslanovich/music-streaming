// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class EventWeightsTests
{
    [Theory]
    [InlineData(0.0, EventWeights.AbandonedWeight)]
    [InlineData(0.04, EventWeights.AbandonedWeight)]
    [InlineData(0.05, EventWeights.DroppedWeight)]
    [InlineData(0.19, EventWeights.DroppedWeight)]
    [InlineData(0.20, EventWeights.PartialWeight)]
    [InlineData(0.49, EventWeights.PartialWeight)]
    [InlineData(0.50, EventWeights.SustainedWeight)]
    [InlineData(0.79, EventWeights.SustainedWeight)]
    [InlineData(0.80, EventWeights.NearCompleteWeight)]
    [InlineData(1.0, EventWeights.NearCompleteWeight)]
    public void Completion_curve_maps_each_band(double ratio, double expected) =>
        Assert.Equal(expected, EventWeights.ForCompletion(ratio));

    [Fact]
    public void Completion_curve_never_decreases()
    {
        var previous = double.NegativeInfinity;

        for (var ratio = 0.0; ratio <= 1.0; ratio += 0.01)
        {
            var weight = EventWeights.ForCompletion(ratio);
            Assert.True(weight >= previous, $"Weight dropped at ratio {ratio}");
            previous = weight;
        }
    }

    [Fact]
    public void Listening_through_and_liking_is_a_strong_positive()
    {
        var total =
            EventWeights.ForTrack(PlaybackEventType.TrackStarted, 0)
            + EventWeights.ForTrack(PlaybackEventType.TrackSkipped, 0.9)
            + EventWeights.ForTrack(PlaybackEventType.TrackCompleted, 1.0)
            + EventWeights.ForTrack(PlaybackEventType.TrackLiked, 0);

        Assert.Equal(4.3, total, precision: 6);
        Assert.True(total > 3);
    }

    [Fact]
    public void Repeatedly_skipping_after_seconds_is_a_strong_negative()
    {
        var ratio = EventWeights.CompletionRatio(listenedSeconds: 5, durationSeconds: 200);
        var total = 3 * EventWeights.ForTrack(PlaybackEventType.TrackSkipped, ratio);

        Assert.Equal(-3.0, total, precision: 6);
    }

    [Theory]
    [InlineData(PlaybackEventType.TrackStarted)]
    [InlineData(PlaybackEventType.TrackPlayed)]
    [InlineData(PlaybackEventType.TrackPaused)]
    public void Intent_and_heartbeats_carry_no_judgement(PlaybackEventType type) =>
        Assert.Equal(0, EventWeights.ForTrack(type, 1.0));

    [Fact]
    public void Unliking_cancels_a_like() =>
        Assert.Equal(
            0,
            EventWeights.ForTrack(PlaybackEventType.TrackLiked, 0)
            + EventWeights.ForTrack(PlaybackEventType.TrackUnliked, 0));

    [Theory]
    [InlineData(PlaybackEventType.ArtistOpened)]
    [InlineData(PlaybackEventType.AlbumOpened)]
    [InlineData(PlaybackEventType.PlaylistOpened)]
    [InlineData(PlaybackEventType.SearchResultClicked)]
    public void Browsing_is_a_weak_but_real_signal(PlaybackEventType type)
    {
        var weight = EventWeights.ForEntity(type);

        Assert.True(weight > 0);
        Assert.True(weight < EventWeights.ForTrack(PlaybackEventType.TrackCompleted, 1.0));
    }

    [Theory]
    [InlineData(100, 200, 0.5)]
    [InlineData(200, 200, 1.0)]
    [InlineData(400, 200, 1.0)]
    [InlineData(0, 200, 0)]
    [InlineData(-5, 200, 0)]
    public void Completion_ratio_is_clamped(int listened, int duration, double expected) =>
        Assert.Equal(expected, EventWeights.CompletionRatio(listened, duration));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Unknown_duration_yields_no_completion(int duration) =>
        Assert.Equal(0, EventWeights.CompletionRatio(120, duration));

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(0.19, true)]
    [InlineData(0.20, false)]
    [InlineData(0.95, false)]
    public void A_skip_only_counts_below_a_fifth(double ratio, bool expected) =>
        Assert.Equal(expected, EventWeights.IsSkip(PlaybackEventType.TrackSkipped, ratio));

    [Fact]
    public void Finishing_a_track_is_never_a_skip() =>
        Assert.False(EventWeights.IsSkip(PlaybackEventType.TrackCompleted, 0.01));

    [Theory]
    [InlineData(PlaybackEventType.TrackCompleted, 1.0)]
    [InlineData(PlaybackEventType.TrackLiked, 0.0)]
    [InlineData(PlaybackEventType.TrackUnliked, 0.0)]
    [InlineData(PlaybackEventType.TrackAddedToPlaylist, 0.0)]
    [InlineData(PlaybackEventType.TrackSkipped, 0.05)]
    public void Strong_feedback_requests_a_fresh_ranking(PlaybackEventType type, double ratio) =>
        Assert.True(EventWeights.ShouldRefreshRecommendations(type, ratio));

    [Theory]
    [InlineData(PlaybackEventType.TrackStarted, 0.0)]
    [InlineData(PlaybackEventType.TrackPlayed, 0.5)]
    [InlineData(PlaybackEventType.TrackPaused, 0.5)]
    [InlineData(PlaybackEventType.ArtistOpened, 0.0)]
    public void Passive_events_do_not_rebuild_rankings(PlaybackEventType type, double ratio) =>
        Assert.False(EventWeights.ShouldRefreshRecommendations(type, ratio));
}
