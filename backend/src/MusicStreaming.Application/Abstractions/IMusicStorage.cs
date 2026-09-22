// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Original track files and raw access to the storage tree. Every path here is relative to the
/// storage root and resolves back inside it — escaping the root is not possible.
/// </summary>
public interface IMusicStorage
{
    Task<StoredFile> SaveTrackAsync(Stream content, string extension, long maxBytes, CancellationToken ct = default);
    Stream? OpenRead(string storageRelativePath);
    string? ResolveExisting(string storageRelativePath);
    string ResolveForWrite(string storageRelativePath);
    void Delete(string storageRelativePath);
}

/// <summary>
/// Cover art and photos: a base file plus its renditions. The album, artist and playlist editors
/// that write these never touch track originals or HLS, so this is a surface of its own.
/// </summary>
public interface IImageStorage
{
    Task<string> SaveCoverAsync(Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default);
    Task<string> SaveArtistImageAsync(Guid artistId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default);
    Task<string> SavePlaylistCoverAsync(Guid playlistId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default);
    string CoverVariantPath(string coverPath, CoverSize size);

    /// <summary>Deletes the base file together with all of its renditions.</summary>
    void DeleteCover(string coverPath);
}

/// <summary>Derived audio: the transcode cache and the HLS layout.</summary>
public interface IHlsStorage
{
    string TranscodePathFor(string contentHash, AudioQuality quality);
    string EnsureHlsVariantDirectory(string contentHash, AudioQuality quality);
    bool HlsVariantReady(string contentHash, AudioQuality quality);
    Stream? OpenHlsFile(string contentHash, AudioQuality quality, string fileName);
    void DeleteTranscodes(string contentHash);
}

public record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
