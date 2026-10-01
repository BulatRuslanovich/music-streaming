// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;

namespace App.Abstractions;

public enum ArtistImageLookupStatus
{
    Found,
    NotFound,
}

public record ArtistImageLookupResult(ArtistImageLookupStatus Status, byte[]? Content)
{
    public static readonly ArtistImageLookupResult NotFound = new(ArtistImageLookupStatus.NotFound, null);
}

public interface IArtistImageProvider
{
    Task<ArtistImageLookupResult> LookupAsync(string artistName, CancellationToken ct);
}

public enum LyricsLookupStatus
{
    Found,
    NotFound,
    Instrumental,
}

public record LyricsLookupResult(LyricsLookupStatus Status, string? Text, bool Synced)
{
    public static readonly LyricsLookupResult NotFound = new(LyricsLookupStatus.NotFound, null, false);
    public static readonly LyricsLookupResult Instrumental = new(LyricsLookupStatus.Instrumental, null, false);
}

public interface ILyricsProvider
{
    Task<LyricsLookupResult> LookupAsync(LyricsQuery query, CancellationToken ct);
}
