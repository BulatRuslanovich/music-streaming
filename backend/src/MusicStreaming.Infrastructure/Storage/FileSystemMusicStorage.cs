// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Storage;


public class FileSystemMusicStorage : IMusicStorage
{
    private const int BufferSize = 64 * 1024;
    private const string MusicDirectory = "music";
    private const string CoverDirectory = "covers";
    private const string ArtistImageDirectory = "artists";
    private const string PlaylistCoverDirectory = "playlists";
    private const string TranscodeDirectory = "transcodes";
    private const string HlsDirectory = "hls";

    private readonly string _root;
    private readonly ILogger<FileSystemMusicStorage> _logger;

    public FileSystemMusicStorage(IOptions<StorageOptions> options, ILogger<FileSystemMusicStorage> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(options.Value.RootPath);

        Directory.CreateDirectory(Path.Combine(_root, MusicDirectory));
        Directory.CreateDirectory(Path.Combine(_root, CoverDirectory));
        Directory.CreateDirectory(Path.Combine(_root, ArtistImageDirectory));
        Directory.CreateDirectory(Path.Combine(_root, PlaylistCoverDirectory));
        Directory.CreateDirectory(Path.Combine(_root, TranscodeDirectory));
        Directory.CreateDirectory(Path.Combine(_root, HlsDirectory));

        _logger.LogInformation("Music storage rooted at {Root}", _root);
    }

    public async Task<StoredFile> SaveTrackAsync(Stream content, string extension, long maxBytes, CancellationToken ct)
    {
        if (!IsSafeExtension(extension))
            throw new ArgumentException($"Rejected storage extension '{extension}'.", nameof(extension));

        var id = Guid.CreateVersion7().ToString("N");

        var relativePath = $"{MusicDirectory}/{id[^2..]}/{id[^4..^2]}/{id}{extension}";
        var absolutePath = ResolveWithinRoot(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        long size = 0;
        byte[] hash;

        try
        {
            await using var target = new FileStream(
                absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: BufferSize, useAsync: true);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];

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
            TryDeleteAbsolute(absolutePath);
            throw;
        }

        return new StoredFile(relativePath, size, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static bool IsSafeExtension(string extension) =>
        extension.Length is > 1 and <= 6
        && extension[0] == '.'
        && extension[1..].All(char.IsAsciiLetterOrDigit);

    public async Task<string> SaveCoverAsync(
        Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken ct = default)
    {
        if (renditions.Count == 0)
            throw new ArgumentException("A cover needs at least one rendition.", nameof(renditions));

        var fullSizePath = $"{CoverDirectory}/{albumId:N}.webp";

        foreach (var rendition in renditions)
        {
            var relativePath = rendition.Edge == CoverVariants.FullEdge
                ? fullSizePath
                : $"{CoverDirectory}/{albumId:N}.thumb.webp";

            await WriteImageAsync(relativePath, rendition.Content, ct);
        }

        return fullSizePath;
    }

    public string CoverVariantPath(string coverPath, CoverSize size) {
        if (size == CoverSize.Full || string.IsNullOrWhiteSpace(coverPath))
        {
            return coverPath;
        }
        else
        {
            return Path.ChangeExtension(coverPath, null) + ".thumb.webp";
        }
    }

    public void DeleteCover(string coverPath)
    {
        Delete(coverPath);
        Delete(CoverVariantPath(coverPath, CoverSize.Thumb));
    }

    public Task<string> SaveArtistImageAsync(
        Guid artistId, byte[] webpContent, CancellationToken ct = default) =>
        WriteImageAsync($"{ArtistImageDirectory}/{artistId:N}.webp", webpContent, ct);

    public Task<string> SavePlaylistCoverAsync(
        Guid playlistId, byte[] webpContent, CancellationToken ct = default) =>
        WriteImageAsync($"{PlaylistCoverDirectory}/{playlistId:N}.webp", webpContent, ct);

    private async Task<string> WriteImageAsync(
        string relativePath, byte[] content, CancellationToken ct)
    {
        var absolutePath = ResolveWithinRoot(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, content, ct);

        return relativePath;
    }

    public Stream? OpenRead(string storageRelativePath)
    {
        var absolutePath = ResolveWithinRoot(storageRelativePath);
        if (!File.Exists(absolutePath))
            return null;

        return new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public string TranscodePathFor(string contentHash, AudioQuality quality) =>
        $"{TranscodeDirectory}/{contentHash}.{quality.ToString().ToLowerInvariant()}.opus";

    public string HlsVariantDirectoryFor(string contentHash, AudioQuality quality)
    {
        var relativePath = $"{HlsDirectory}/{contentHash}/{quality.ToString().ToLowerInvariant()}";
        var absolutePath = ResolveWithinRoot(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        return absolutePath;
    }

    public bool HlsVariantReady(string contentHash, AudioQuality quality)
    {
        var directory = HlsVariantDirectoryFor(contentHash, quality);
        return File.Exists(Path.Combine(directory, "index.m3u8"))
               && File.Exists(Path.Combine(directory, "init.mp4"))
               && Directory.EnumerateFiles(directory, "segment-*.m4s").Any();
    }

    public Stream? OpenHlsFile(string contentHash, AudioQuality quality, string fileName)
    {
        var directory = HlsVariantDirectoryFor(contentHash, quality);
        var absolutePath = Path.GetFullPath(Path.Combine(directory, fileName));
        var directoryWithSeparator = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(directoryWithSeparator, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Rejected HLS asset path '{fileName}'.");

        if (!File.Exists(absolutePath))
            return null;

        return new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void DeleteTranscodes(string contentHash)
    {
        foreach (var quality in Enum.GetValues<AudioQuality>())
            Delete(TranscodePathFor(contentHash, quality));

        TryDeleteDirectory(ResolveWithinRoot($"{HlsDirectory}/{contentHash}"));
    }

    public string? ResolveExisting(string storageRelativePath)
    {
        var absolutePath = ResolveWithinRoot(storageRelativePath);
        return File.Exists(absolutePath) ? absolutePath : null;
    }

    public string ResolveForWrite(string storageRelativePath)
    {
        var absolutePath = ResolveWithinRoot(storageRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        return absolutePath;
    }

    public void Delete(string storageRelativePath)
    {
        if (string.IsNullOrWhiteSpace(storageRelativePath))
            return;

        try
        {
            TryDeleteAbsolute(ResolveWithinRoot(storageRelativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not delete {Path} from storage", storageRelativePath);
        }
    }

    private string ResolveWithinRoot(string storageRelativePath)
    {
        if (string.IsNullOrWhiteSpace(storageRelativePath))
            throw new UnauthorizedAccessException("An empty storage path is not valid.");

        if (Path.IsPathRooted(storageRelativePath) || storageRelativePath.Contains(':'))
            throw new UnauthorizedAccessException($"Rejected absolute storage path '{storageRelativePath}'.");

        var candidate = Path.GetFullPath(Path.Combine(_root, storageRelativePath));

        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Blocked path traversal attempt: {Requested} resolved to {Resolved}",
                storageRelativePath, candidate);
            throw new UnauthorizedAccessException($"Rejected storage path '{storageRelativePath}'.");
        }

        return candidate;
    }

    private static void TryDeleteAbsolute(string absolutePath)
    {
        if (File.Exists(absolutePath))
            File.Delete(absolutePath);
    }

    private void TryDeleteDirectory(string absolutePath)
    {
        try
        {
            if (Directory.Exists(absolutePath))
                Directory.Delete(absolutePath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not delete storage directory {Path}", absolutePath);
        }
    }
}
