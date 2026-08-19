// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;

namespace MusicStreaming.IntegrationTests;

internal static class Cancel
{
    public static CancellationToken Token => TestContext.Current.CancellationToken;
}
