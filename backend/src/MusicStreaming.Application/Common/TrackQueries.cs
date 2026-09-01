// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Linq.Expressions;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Общие куски запросов по трекам.
/// </summary>
/// <remarks>
/// У трека может не быть строки статистики: она появляется первым проходом пересчёта, а до него
/// трек всё равно должен попадать в выдачу. Что «нет строки» значит ноль, а не исключение из
/// сортировки — одно решение на систему; раньше оно было переписано в семи местах.
/// </remarks>
public static class TrackQueries
{
    public static Expression<Func<Track, double>> Popularity =>
        track => track.Stats == null ? 0 : track.Stats.PopularityScore;

    public static Expression<Func<Track, int>> Plays =>
        track => track.Stats == null ? 0 : track.Stats.PlayCount;

    /// <summary>
    /// Порядок «сначала то, что слушают, при равенстве — новее». Прелюдия трёх источников
    /// кандидатов и топа артиста; менять её здесь — значит менять их все сразу, и это намеренно.
    /// </summary>
    public static IOrderedQueryable<Track> ByPopularityThenNewest(this IQueryable<Track> tracks) =>
        tracks.OrderByDescending(Popularity).ThenByDescending(track => track.CreatedAt);
}
