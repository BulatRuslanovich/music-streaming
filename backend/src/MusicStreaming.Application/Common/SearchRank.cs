// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public static class SearchRank
{
    public const string FunctionName = "search_rank";
    public const int Exact = 0;
    public const int Prefix = 1;
    public const int WordPrefix = 2;
    public const int Contains = 3;
    public const int Unrelated = 4;
    public static int Of(string value, string term) =>
        throw new NotSupportedException($"{FunctionName} is evaluated by the database.");
}
