// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Scoring;
using MusicStreaming.Domain.Entities.Recommendations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class TasteSignalTests
{
    [Fact]
    public void An_abandoned_track_pushes_the_vector_away()
    {
        // Брошено на 10% — почти полный разворот.
        Assert.Equal(-0.9, TasteSignal.WeightFor(PlaybackEventType.TrackSkipped, 0.1), precision: 6);
    }

    [Fact]
    public void A_skip_near_the_end_is_not_a_rejection()
    {
        // Промотать последние проценты — это не «не нравится».
        Assert.True(TasteSignal.WeightFor(PlaybackEventType.TrackSkipped, 0.9) > 0.8);
    }

    [Fact]
    public void A_skip_midway_is_mildly_positive_rather_than_negative()
    {
        // Здесь шкала расходится с EventWeights намеренно: сдвиг единичного вектора
        // на маленький минус — это дисперсия, а не сигнал.
        var weight = TasteSignal.WeightFor(PlaybackEventType.TrackSkipped, 0.5);

        Assert.True(weight > 0);
        Assert.True(weight < 0.2);
    }

    [Fact]
    public void Starting_a_track_says_nothing_about_taste()
    {
        // Иначе вектор стал бы средним по истории воспроизведения, а не вкусом.
        Assert.Equal(0, TasteSignal.WeightFor(PlaybackEventType.TrackStarted, 0.0));
        Assert.Equal(0, TasteSignal.WeightFor(PlaybackEventType.TrackStarted, 1.0));
        Assert.Equal(0, TasteSignal.WeightFor(PlaybackEventType.TrackPaused, 0.5));
    }

    [Fact]
    public void Finishing_a_track_counts_fully()
    {
        Assert.Equal(1.0, TasteSignal.WeightFor(PlaybackEventType.TrackCompleted, 1.0), precision: 6);
    }

    [Fact]
    public void A_barely_played_track_barely_counts()
    {
        var weight = TasteSignal.WeightFor(PlaybackEventType.TrackCompleted, 0.1);

        Assert.True(weight > 0);
        Assert.True(weight < 0.02);
    }

    [Fact]
    public void An_explicit_rating_outweighs_any_amount_of_listening()
    {
        var finished = TasteSignal.WeightFor(PlaybackEventType.TrackCompleted, 1.0);

        Assert.True(TasteSignal.WeightFor(PlaybackEventType.TrackLiked, 0) > finished);
        Assert.Equal(-TasteSignal.LikeWeight, TasteSignal.WeightFor(PlaybackEventType.TrackUnliked, 0));
    }

    [Fact]
    public void Playlist_membership_is_a_deliberate_act_and_weighs_more_than_a_play()
    {
        var added = TasteSignal.WeightFor(PlaybackEventType.TrackAddedToPlaylist, 0);

        Assert.True(added > TasteSignal.WeightFor(PlaybackEventType.TrackCompleted, 1.0));
        Assert.True(TasteSignal.WeightFor(PlaybackEventType.TrackRemovedFromPlaylist, 0) < 0);
    }

    [Fact]
    public void The_two_scales_agree_that_an_untouched_track_is_a_rejection()
    {
        // Единственная точка, где TasteSignal и EventWeights обязаны совпадать.
        Assert.Equal(
            EventWeights.ForTrack(PlaybackEventType.TrackSkipped, 0),
            TasteSignal.WeightFor(PlaybackEventType.TrackSkipped, 0),
            precision: 6);
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(2.5)]
    public void A_nonsensical_ratio_is_clamped_rather_than_propagated(double ratio)
    {
        var weight = TasteSignal.WeightFor(PlaybackEventType.TrackCompleted, ratio);

        Assert.InRange(weight, 0, 1);
    }

    [Fact]
    public void Events_about_entities_never_move_the_vector()
    {
        Assert.Equal(0, TasteSignal.WeightFor(PlaybackEventType.ArtistOpened, 1.0));
        Assert.Equal(0, TasteSignal.WeightFor(PlaybackEventType.AlbumOpened, 1.0));
        Assert.Equal(0, TasteSignal.WeightFor(PlaybackEventType.SearchResultClicked, 1.0));
    }
}
