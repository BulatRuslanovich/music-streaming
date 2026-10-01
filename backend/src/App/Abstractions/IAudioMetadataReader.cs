// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Domain.Entities;

namespace App.Abstractions;

public interface IAudioMetadataReader
{
    AudioMetadata? Read(string absolutePath, string tagLibMimeType);
}

public record AudioMetadata(
    string? Title,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> AlbumArtists,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    int DurationSeconds,
    byte[]? CoverData,
    string? CoverMimeType,
    string? Lyrics,
    IReadOnlyList<LyricLine> SyncedLyrics,
    string? Codec,
    int? BitrateKbps,
    int? SampleRateHz,
    int? BitsPerSample);
