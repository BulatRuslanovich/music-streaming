// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Security.Cryptography;
using App.Abstractions;
using App.Common;

namespace Infrastructure.Storage;

public class FileSystemMusicStorage(StorageRoot root) : IMusicStorage
{
    public long AvailableBytes() => new DriveInfo(root.Resolve(StorageRoot.MusicDirectory)).AvailableFreeSpace;

    public async Task<StoredFile> SaveTrackAsync(
        Stream content, string extension, long maxBytes, CancellationToken ct = default)
    {
        if (extension.Length is < 2 or > 6 || extension[0] != '.' || !extension[1..].All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException($"Rejected storage extension '{extension}'.", nameof(extension));

        var id = Guid.CreateVersion7().ToString("N");

        var relativePath = $"{StorageRoot.MusicDirectory}/{id[^2..]}/{id[^4..^2]}/{id}{extension}";
        var absolutePath = root.Resolve(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        long size = 0;
        byte[] hash;

        try
        {
            await using var target = new FileStream(
                absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: StorageRoot.BufferSize, useAsync: true);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[StorageRoot.BufferSize];

            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0)
                    break;

                size += read;
                if (size > maxBytes)
                    throw new UploadTooLargeException(maxBytes);

                hasher.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            await target.FlushAsync(ct);
            hash = hasher.GetHashAndReset();
        }
        catch
        {
            StorageRoot.TryDeleteAbsolute(absolutePath);
            throw;
        }

        return new StoredFile(relativePath, size, Convert.ToHexString(hash).ToLowerInvariant());
    }

    public Stream? OpenRead(string storageRelativePath) => StorageRoot.OpenAbsolute(root.Resolve(storageRelativePath));

    public string? ResolveExisting(string storageRelativePath) =>
        root.Resolve(storageRelativePath) is var absolutePath && File.Exists(absolutePath) ? absolutePath : null;

    public void Delete(string storageRelativePath) => root.Delete(storageRelativePath);
}
