// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MusicStreaming.Infrastructure.Persistence.Configurations;

internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<IReadOnlyList<T>, string> Converter<T>() => new(
        list => JsonSerializer.Serialize(list, Options),
        json => JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>());

    public static ValueComparer<IReadOnlyList<T>> Comparer<T>() => new(
        (left, right) => left != null && right != null ? left.SequenceEqual(right) : left == right,
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        list => list.ToList());
}
