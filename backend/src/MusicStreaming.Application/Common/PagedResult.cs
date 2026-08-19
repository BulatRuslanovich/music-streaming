// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public record PageRequest
{
    public const int MaxPageSize = 200;

    public int Page { get; }
    public int PageSize { get; }

    public PageRequest(int? page = null, int? pageSize = null)
    {
        Page = page is null or < 1 ? 1 : page.Value;
        PageSize = pageSize is null or < 1 ? 50 : Math.Min(pageSize.Value, MaxPageSize);
    }

    public int Skip => (Page - 1) * PageSize;
}
