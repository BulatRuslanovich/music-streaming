// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Abstractions;

public enum ArtistImageLookupStatus
{
    Found,
    NotFound,
    Ambiguous,
}

public record ArtistImageLookupResult(ArtistImageLookupStatus Status, byte[]? Content)
{
    public static readonly ArtistImageLookupResult NotFound = new(ArtistImageLookupStatus.NotFound, null);
    public static readonly ArtistImageLookupResult Ambiguous = new(ArtistImageLookupStatus.Ambiguous, null);
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

/// <summary>Вес тега приходит от провайдера в его собственной шкале; наружу отдаётся 0..1.</summary>
public record ProviderTag(string Name, double Weight);

public interface IMusicTagProvider
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<ProviderTag>> ArtistTagsAsync(string artistName, CancellationToken ct);

    Task<IReadOnlyList<ProviderTag>> TrackTagsAsync(
        string artistName, string title, CancellationToken ct);
}
