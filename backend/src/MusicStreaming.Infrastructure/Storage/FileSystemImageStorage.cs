// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Storage;

/// <summary>Обложки альбомов, фото артистов и обложки плейлистов — базовый файл плюс рендишены.</summary>
public class FileSystemImageStorage(StorageRoot root) : IImageStorage
{
    public Task<string> SaveCoverAsync(
        Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default) =>
        SaveRenditionsAsync($"{StorageRoot.CoverDirectory}/{albumId:N}.webp", renditions, ct);

    public Task<string> SaveArtistImageAsync(
        Guid artistId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default) =>
        SaveRenditionsAsync($"{StorageRoot.ArtistImageDirectory}/{artistId:N}.webp", renditions, ct);

    public Task<string> SavePlaylistCoverAsync(
        Guid playlistId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default) =>
        SaveRenditionsAsync($"{StorageRoot.PlaylistCoverDirectory}/{playlistId:N}.webp", renditions, ct);

    private async Task<string> SaveRenditionsAsync(
        string fullSizePath, IReadOnlyList<ResizedImage> renditions, CancellationToken ct)
    {
        if (renditions.Count == 0)
            throw new ArgumentException("An image needs at least one rendition.", nameof(renditions));

        // Базовым становится самый крупный рендишен, не считая «большого». Привязка к самому
        // числу FullEdge здесь не работает: крупный рендишен появляется не всегда, а у мелкого
        // источника не будет и рендишена в 640 — и тогда базовый файл, на который смотрит
        // Album.CoverPath, просто не был бы записан.
        var baseEdge = renditions
            .Select(rendition => rendition.Edge)
            .Where(edge => edge != CoverVariants.LargeEdge)
            .DefaultIfEmpty(renditions.Max(rendition => rendition.Edge))
            .Max();

        foreach (var rendition in renditions)
        {
            var relativePath = rendition.Edge == baseEdge
                ? fullSizePath
                : CoverVariantPath(
                    fullSizePath,
                    rendition.Edge == CoverVariants.LargeEdge ? CoverSize.Large : CoverSize.Thumb);

            await WriteImageAsync(relativePath, rendition.Content, ct);
        }

        return fullSizePath;
    }

    public string CoverVariantPath(string coverPath, CoverSize size)
    {
        if (size == CoverSize.Full || string.IsNullOrWhiteSpace(coverPath))
            return coverPath;

        var suffix = size == CoverSize.Large ? ".large.webp" : ".thumb.webp";

        return Path.ChangeExtension(coverPath, null) + suffix;
    }

    public void DeleteCover(string coverPath)
    {
        root.Delete(coverPath);
        root.Delete(CoverVariantPath(coverPath, CoverSize.Thumb));
        root.Delete(CoverVariantPath(coverPath, CoverSize.Large));
    }

    private async Task WriteImageAsync(string relativePath, byte[] content, CancellationToken ct)
    {
        var absolutePath = root.Resolve(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, content, ct);
    }
}
