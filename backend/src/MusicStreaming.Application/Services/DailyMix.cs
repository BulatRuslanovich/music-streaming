// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Buffers.Binary;

namespace MusicStreaming.Application.Services;

public static class DailyMix
{
    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    public static IReadOnlyList<Guid> Pick(
        Guid userId, DateOnly localDate, IEnumerable<Guid> pool, int size)
    {
        if (size <= 0)
            return [];

        var seed = Seed(userId, localDate);

        return pool
            .Distinct()
            .OrderBy(id => OrderKey(seed, id))
            .ThenBy(id => id)
            .Take(size)
            .ToList();
    }

    private static ulong Seed(Guid userId, DateOnly localDate)
    {
        Span<byte> user = stackalloc byte[16];
        userId.TryWriteBytes(user, bigEndian: true, out _);

        Span<byte> day = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(day, localDate.DayNumber);

        return Hash(Hash(FnvOffset, user), day);
    }

    private static ulong OrderKey(ulong seed, Guid trackId)
    {
        Span<byte> track = stackalloc byte[16];
        trackId.TryWriteBytes(track, bigEndian: true, out _);

        return Hash(seed, track);
    }

    private static ulong Hash(ulong start, ReadOnlySpan<byte> data)
    {
        var hash = start;

        foreach (var value in data)
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        return hash;
    }
}
