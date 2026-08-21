// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace MusicStreaming.Application.Common;

public static class Paging
{
    public static async Task<PagedResult<TResult>> ToPagedAsync<TSource, TResult>(
        this IQueryable<TSource> query,
        PageRequest page,
        Expression<Func<TSource, TResult>> projection,
        CancellationToken ct = default)
    {
        //INFO: По сути юзеров не так много, так что не страшно тянуть кол-во
        var total = await query.CountAsync(ct);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(projection)
            .ToListAsync(ct);

        return new PagedResult<TResult>(items, total, page.Page, page.PageSize);
    }
}
