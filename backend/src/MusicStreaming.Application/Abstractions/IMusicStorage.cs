// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Abstractions;

public interface IMusicStorage
{
    Task<StoredFile> SaveTrackAsync(Stream content, string extension, long maxBytes, CancellationToken ct = default);
    Stream? OpenRead(string storageRelativePath);
    string? ResolveExisting(string storageRelativePath);
    void Delete(string storageRelativePath);
}

public interface IImageStorage
{
    Task<string> SaveCoverAsync(Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default);
    Task<string> SaveArtistImageAsync(Guid artistId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default);
    Task<string> SavePlaylistCoverAsync(Guid playlistId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default);
    string CoverVariantPath(string coverPath, CoverSize size);

    void DeleteCover(string coverPath);
}

public interface IHlsStorage
{
    string EnsureHlsVariantDirectory(string contentHash, AudioQuality quality);
    bool HlsVariantReady(string contentHash, AudioQuality quality);
    Stream? OpenHlsFile(string contentHash, AudioQuality quality, string fileName);
    void DeleteTranscodes(string contentHash);
}

public record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
