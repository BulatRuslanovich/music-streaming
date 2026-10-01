// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Common;

namespace Infrastructure.Storage;

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

            var absolutePath = root.Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllBytesAsync(absolutePath, rendition.Content, ct);
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
}
