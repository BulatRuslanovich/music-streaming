// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Buffers.Binary;

namespace MusicStreaming.Application.Services;

public static class DailyMix
{
    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;
    private const double MinimumWeight = 0.02;

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

    /// <summary>
    /// Взвешенная выборка без возвращения (схема Эфраимидиса — Спиракиса): ключ элемента это
    /// <c>u^(1/w)</c>, где <c>u</c> детерминированно выводится из того же хеша. Микс остаётся
    /// стабильным в течение дня и разным у разных пользователей, но при этом наверх попадает
    /// то, что действительно выше по скору, а не равномерно перемешанный пул.
    /// </summary>
    public static IReadOnlyList<Guid> PickWeighted(
        Guid userId, DateOnly localDate, IEnumerable<(Guid Id, double Weight)> pool, int size)
    {
        if (size <= 0)
            return [];

        var seed = Seed(userId, localDate);

        var best = new Dictionary<Guid, double>();

        foreach (var (id, weight) in pool)
        {
            if (!best.TryGetValue(id, out var existing) || weight > existing)
                best[id] = weight;
        }

        return best
            .OrderByDescending(pair => SamplingKey(seed, pair.Key, pair.Value))
            .ThenBy(pair => pair.Key)
            .Take(size)
            .Select(pair => pair.Key)
            .ToList();
    }

    private static double SamplingKey(ulong seed, Guid trackId, double weight)
    {
        // Скор кандидата может быть нулевым или отрицательным — оставляем каждому минимальный шанс.
        var effective = Math.Max(weight, 0) + MinimumWeight;

        // u равномерно в (0, 1]; ln(u)/w монотонно эквивалентно u^(1/w), но без потери точности.
        var u = (OrderKey(seed, trackId) + 1.0) / (ulong.MaxValue + 1.0);

        return Math.Log(Math.Clamp(u, double.Epsilon, 1)) / effective;
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

        return Avalanche(Hash(seed, track));
    }

    // FNV-1a почти не размешивает старшие биты: у близких идентификаторов различаются только младшие.
    // Пока ключи сравнивались целиком, это сходило с рук, но взвешенной выборке нужен равномерный
    // разброс по всей ширине слова, иначе весь случайный вклад схлопывается в одно значение.
    private static ulong Avalanche(ulong value)
    {
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccd;
        value ^= value >> 33;
        value *= 0xc4ceb9fe1a85ec53;
        value ^= value >> 33;

        return value;
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
