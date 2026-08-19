// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Infrastructure.Integrations;

public static class OutboundRetry
{
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    ];

    public static TimeSpan? DelayFor(OutboundJobKind kind, int attempts, LastfmException failure)
    {
        if (!failure.IsTransient || failure.IsAuthFailure)
            return null;

        if (kind == OutboundJobKind.LastfmNowPlaying)
            return null;

        return attempts >= 1 && attempts <= Backoff.Length ? Backoff[attempts - 1] : null;
    }
}
