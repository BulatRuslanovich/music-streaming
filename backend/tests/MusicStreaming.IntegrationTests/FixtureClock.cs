// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Системные часы, которые тест может ненадолго остановить в нужной точке.
///
/// Нужны там, где поведение зависит от календаря, а не от длительности: итоги месяца
/// открываются только первого-седьмого числа, и без подмены такой тест был бы зелёным
/// меньше четверти месяца.
/// </summary>
public sealed class FixtureClock : TimeProvider
{
    private DateTimeOffset? _pinned;

    public override DateTimeOffset GetUtcNow() => _pinned ?? base.GetUtcNow();

    /// <summary>Держит время на месте, пока результат не освободят.</summary>
    public IDisposable PinnedAt(DateTimeOffset moment)
    {
        _pinned = moment;
        return new Release(this);
    }

    private sealed class Release(FixtureClock clock) : IDisposable
    {
        public void Dispose() => clock._pinned = null;
    }
}
