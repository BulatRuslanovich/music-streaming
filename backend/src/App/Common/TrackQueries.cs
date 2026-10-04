// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Linq.Expressions;
using Domain.Entities;

namespace App.Common;

public static class TrackQueries
{
    public static Expression<Func<Track, double>> Popularity =>
        track => track.Stats == null ? 0 : track.Stats.PopularityScore;

    public static IOrderedQueryable<Track> ByPopularityThenNewest(this IQueryable<Track> tracks) =>
        tracks.OrderByDescending(Popularity).ThenByDescending(track => track.CreatedAt);
}
