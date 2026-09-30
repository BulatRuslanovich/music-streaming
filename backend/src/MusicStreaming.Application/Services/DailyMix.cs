// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Buffers.Binary;

namespace MusicStreaming.Application.Services;

public static class DailyMix
{
    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;
    private const double MinimumWeight = 0.02;

    public static IReadOnlyList<Guid> PickWeighted(
        Guid userId, DateOnly localDate, IEnumerable<(Guid Id, double Weight)> pool, int size)
    {
        if (size <= 0)
            return [];

        var best = new Dictionary<Guid, double>();

        foreach (var (id, weight) in pool)
        {
            if (!best.TryGetValue(id, out var existing) || weight > existing)
                best[id] = weight;
        }

        Span<byte> bytes = stackalloc byte[16];
        userId.TryWriteBytes(bytes, bigEndian: true, out _);
        var seed = Hash(FnvOffset, bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes, localDate.DayNumber);
        seed = Hash(seed, bytes[..4]);

        var keyed = new List<(Guid Id, double Key)>(best.Count);

        foreach (var (id, weight) in best)
        {
            id.TryWriteBytes(bytes, bigEndian: true, out _);
            var hash = Hash(seed, bytes);

            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccd;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53;
            hash ^= hash >> 33;

            var u = (hash + 1.0) / (ulong.MaxValue + 1.0);
            keyed.Add((id, Math.Log(Math.Clamp(u, double.Epsilon, 1)) / (Math.Max(weight, 0) + MinimumWeight)));
        }

        return keyed
            .OrderByDescending(item => item.Key)
            .ThenBy(item => item.Id)
            .Take(size)
            .Select(item => item.Id)
            .ToList();
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
