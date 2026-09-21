// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Application.Recommendations.Scoring;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class TasteVectorMathTests
{
    private const double Alpha = 0.22;

    [Fact]
    public void The_first_signal_defines_the_taste_outright()
    {
        var folded = TasteVectorMath.Fold([], [0.6f, 0.8f], signedWeight: 1.0, Alpha);

        Assert.Equal(0.6f, folded[0], precision: 5);
        Assert.Equal(0.8f, folded[1], precision: 5);
    }

    [Fact]
    public void A_first_signal_that_is_negative_points_the_other_way()
    {
        var folded = TasteVectorMath.Fold([], [1f, 0f], signedWeight: -1.0, Alpha);

        Assert.Equal(-1f, folded[0], precision: 5);
    }

    [Fact]
    public void A_positive_signal_pulls_the_vector_towards_the_track()
    {
        var before = VectorMath.Normalized([1f, 0f]);
        var track = VectorMath.Normalized([0f, 1f]);

        var after = TasteVectorMath.Fold(before, track, signedWeight: 1.0, Alpha);

        Assert.True(VectorMath.Dot(after, track) > VectorMath.Dot(before, track));
    }

    [Fact]
    public void A_negative_signal_pushes_the_vector_away()
    {
        var before = VectorMath.Normalized([1f, 1f]);
        var track = VectorMath.Normalized([0f, 1f]);

        var after = TasteVectorMath.Fold(before, track, signedWeight: -1.0, Alpha);

        Assert.True(VectorMath.Dot(after, track) < VectorMath.Dot(before, track));
    }

    [Fact]
    public void The_result_is_always_a_unit_vector()
    {
        var vector = VectorMath.Normalized([1f, 0f, 0f]);

        foreach (var weight in new[] { 2.0, -2.0, 0.3, 1.0 })
        {
            vector = TasteVectorMath.Fold(vector, VectorMath.Normalized([0.2f, 0.9f, -0.1f]), weight, Alpha);
            Assert.Equal(1.0, Math.Sqrt(VectorMath.Dot(vector, vector)), precision: 4);
        }
    }

    [Fact]
    public void A_like_pulls_harder_than_a_finished_play()
    {
        var before = VectorMath.Normalized([1f, 0f]);
        var track = VectorMath.Normalized([0f, 1f]);

        var afterPlay = TasteVectorMath.Fold(before, track, TasteSignal.WeightFor(
            Domain.Entities.Recommendations.PlaybackEventType.TrackCompleted, 1.0), Alpha);
        var afterLike = TasteVectorMath.Fold(before, track, TasteSignal.LikeWeight, Alpha);

        Assert.True(VectorMath.Dot(afterLike, track) > VectorMath.Dot(afterPlay, track));
    }

    [Fact]
    public void A_zero_weight_changes_nothing()
    {
        var before = VectorMath.Normalized([0.3f, 0.7f]);

        Assert.Equal(before, TasteVectorMath.Fold(before, [1f, 0f], signedWeight: 0, Alpha));
    }

    [Fact]
    public void A_missing_track_vector_changes_nothing()
    {
        var before = VectorMath.Normalized([0.3f, 0.7f]);

        Assert.Equal(before, TasteVectorMath.Fold(before, [], signedWeight: 1.0, Alpha));
    }

    [Fact]
    public void A_dimension_change_reseeds_rather_than_mixing_incompatible_spaces()
    {
        // Смена модели на полпути: складывать 2-мерный вкус с 3-мерным треком нельзя.
        var folded = TasteVectorMath.Fold([1f, 0f], VectorMath.Normalized([0f, 1f, 0f]), 1.0, Alpha);

        Assert.Equal(3, folded.Length);
        Assert.Equal(1f, folded[1], precision: 5);
    }

    [Fact]
    public void Repeated_signals_converge_on_the_track_they_keep_pointing_at()
    {
        var vector = VectorMath.Normalized([1f, 0f]);
        var track = VectorMath.Normalized([0f, 1f]);

        for (var i = 0; i < 20; i++)
            vector = TasteVectorMath.Fold(vector, track, signedWeight: 1.0, Alpha);

        // alpha = 0.22 забывает быстро: двадцати событий достаточно, чтобы прийти почти вплотную.
        Assert.True(VectorMath.Dot(vector, track) > 0.99);
    }
}
