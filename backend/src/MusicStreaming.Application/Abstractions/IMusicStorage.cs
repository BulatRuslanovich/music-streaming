// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Оригиналы треков и сырой доступ к файлам хранилища. Все пути здесь относительны корню
/// хранилища и разрешаются обратно внутрь него — выйти за корень нельзя.
/// </summary>
public interface IMusicStorage
{
    Task<StoredFile> SaveTrackAsync(Stream content, string extension, long maxBytes, CancellationToken cancellationToken = default);
    Stream? OpenRead(string storageRelativePath);
    string? ResolveExisting(string storageRelativePath);
    string ResolveForWrite(string storageRelativePath);
    void Delete(string storageRelativePath);
}

/// <summary>
/// Обложки и фото: базовый файл плюс его рендишены. Кто их пишет — редакторы альбома, артиста и
/// плейлиста — оригиналов треков и HLS не касается вовсе, поэтому это отдельная поверхность.
/// </summary>
public interface IImageStorage
{
    Task<string> SaveCoverAsync(Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken cancellationToken = default);
    Task<string> SaveArtistImageAsync(Guid artistId, IReadOnlyList<ResizedImage> renditions, CancellationToken cancellationToken = default);
    Task<string> SavePlaylistCoverAsync(Guid playlistId, IReadOnlyList<ResizedImage> renditions, CancellationToken cancellationToken = default);
    string CoverVariantPath(string coverPath, CoverSize size);

    /// <summary>Удаляет базовый файл вместе со всеми его рендишенами.</summary>
    void DeleteCover(string coverPath);
}

/// <summary>Производное аудио: кэш перекодировок и раскладка HLS.</summary>
public interface IHlsStorage
{
    string TranscodePathFor(string contentHash, AudioQuality quality);
    string EnsureHlsVariantDirectory(string contentHash, AudioQuality quality);
    bool HlsVariantReady(string contentHash, AudioQuality quality);
    Stream? OpenHlsFile(string contentHash, AudioQuality quality, string fileName);
    void DeleteTranscodes(string contentHash);
}

public record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
