// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Recommendations.Embeddings;
using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class VectorMathTests
{
    [Fact]
    public void Normalisation_produces_unit_length()
    {
        var vector = VectorMath.Normalized([3f, 4f]);

        Assert.Equal(0.6f, vector[0], precision: 5);
        Assert.Equal(0.8f, vector[1], precision: 5);
    }

    [Fact]
    public void A_degenerate_vector_is_left_alone_rather_than_divided_by_nothing()
    {
        var vector = VectorMath.Normalized([0f, 0f, 0f]);

        Assert.All(vector, component => Assert.Equal(0f, component));
    }

    [Fact]
    public void A_blend_leans_towards_the_heavier_side()
    {
        var blended = VectorMath.Blend([1f, 0f], 0.7f, [0f, 1f], 0.3f);

        Assert.True(blended[0] > blended[1]);
        Assert.Equal(1.0, Math.Sqrt(VectorMath.Dot(blended, blended)), precision: 5);
    }

    [Fact]
    public void A_blend_with_a_missing_side_returns_the_side_that_is_there()
    {
        Assert.Equal(VectorMath.Normalized([3f, 4f]), VectorMath.Blend([3f, 4f], 0.7f, [], 0.3f));
        Assert.Equal(VectorMath.Normalized([3f, 4f]), VectorMath.Blend([], 0.7f, [3f, 4f], 0.3f));
    }

    [Fact]
    public void A_blend_of_mismatched_dimensions_keeps_the_left_side()
    {
        // Смена модели на полпути не должна складывать вектора разной размерности.
        Assert.Equal(VectorMath.Normalized([1f, 0f]), VectorMath.Blend([1f, 0f], 0.7f, [0f, 1f, 0f], 0.3f));
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.25, 2.0)]
    [InlineData(0.5, 3.0)]
    [InlineData(1.0, 5.0)]
    public void Quantiles_interpolate_linearly_the_way_numpy_does(double q, double expected)
    {
        // numpy.quantile([1,2,3,4,5], q) по умолчанию — линейная интерполяция.
        Assert.Equal(expected, VectorMath.Quantile([1f, 2f, 3f, 4f, 5f], q), precision: 5);
    }

    [Fact]
    public void A_quantile_interpolates_between_neighbours()
    {
        // Позиция 0.25 * 3 = 0.75, то есть три четверти пути от 10 к 20.
        Assert.Equal(17.5, VectorMath.Quantile([10f, 20f, 30f, 40f], 0.25), precision: 4);
    }

    [Fact]
    public void A_quantile_of_nothing_is_zero_and_of_one_value_is_that_value()
    {
        Assert.Equal(0f, VectorMath.Quantile([], 0.5));
        Assert.Equal(7f, VectorMath.Quantile([7f], 0.25));
    }

    [Fact]
    public void A_quantile_does_not_disturb_the_caller_s_data()
    {
        var values = new[] { 5f, 1f, 3f };

        VectorMath.Quantile(values, 0.5);

        Assert.Equal([5f, 1f, 3f], values);
    }

    [Fact]
    public void The_stand_in_embedder_is_deterministic_and_unit_length()
    {
        var first = DeterministicAudioEmbedder.VectorFor("/music/aa/bb/track.flac", 512);
        var second = DeterministicAudioEmbedder.VectorFor("/music/aa/bb/track.flac", 512);
        var other = DeterministicAudioEmbedder.VectorFor("/music/cc/dd/other.flac", 512);

        Assert.Equal(first, second);
        Assert.Equal(1.0, Math.Sqrt(VectorMath.Dot(first, first)), precision: 4);

        // Разные ключи дают по существу несвязанные направления — в 512 измерениях
        // случайные вектора почти ортогональны.
        Assert.InRange(Math.Abs(VectorMath.Dot(first, other)), 0, 0.2);
    }
}
