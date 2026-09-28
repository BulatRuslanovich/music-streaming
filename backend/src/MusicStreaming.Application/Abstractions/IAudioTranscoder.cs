// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public interface IAudioTranscoder
{
    bool IsAvailable { get; }

    Task<bool> TranscodeToHlsAsync(
        string sourceAbsolutePath,
        string targetDirectory,
        int bitrateKbps,
        CancellationToken ct = default);
}
