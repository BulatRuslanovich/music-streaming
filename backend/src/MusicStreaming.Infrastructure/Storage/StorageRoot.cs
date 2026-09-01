// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Storage;

/// <summary>
/// Корень хранилища и единственное место, где относительный путь превращается в абсолютный.
/// </summary>
/// <remarks>
/// Три реализации хранилища — оригиналы, картинки и HLS — делят один корень и одно правило
/// разрешения путей. Правило здесь ровно одно на всех намеренно: это граница безопасности, и
/// «почти такая же» проверка во второй копии рано или поздно разойдётся с первой.
/// </remarks>
public sealed class StorageRoot
{
    public const int BufferSize = 64 * 1024;

    public const string MusicDirectory = "music";
    public const string CoverDirectory = "covers";
    public const string ArtistImageDirectory = "artists";
    public const string PlaylistCoverDirectory = "playlists";
    public const string TranscodeDirectory = "transcodes";
    public const string HlsDirectory = "hls";

    private readonly string _root;
    private readonly ILogger<StorageRoot> _logger;

    public StorageRoot(IOptions<StorageOptions> options, ILogger<StorageRoot> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(options.Value.RootPath);

        foreach (var directory in (string[])
                 [
                     MusicDirectory, CoverDirectory, ArtistImageDirectory,
                     PlaylistCoverDirectory, TranscodeDirectory, HlsDirectory,
                 ])
        {
            Directory.CreateDirectory(Path.Combine(_root, directory));
        }

        _logger.LogInformation("Music storage rooted at {Root}", _root);
    }

    public string Resolve(string storageRelativePath)
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

    public string? ResolveExisting(string storageRelativePath)
    {
        var absolutePath = Resolve(storageRelativePath);
        return File.Exists(absolutePath) ? absolutePath : null;
    }

    public string ResolveForWrite(string storageRelativePath)
    {
        var absolutePath = Resolve(storageRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        return absolutePath;
    }

    public Stream? OpenRead(string storageRelativePath) => OpenAbsolute(Resolve(storageRelativePath));

    public static Stream? OpenAbsolute(string absolutePath)
    {
        if (!File.Exists(absolutePath))
            return null;

        return new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public void Delete(string storageRelativePath)
    {
        if (string.IsNullOrWhiteSpace(storageRelativePath))
            return;

        try
        {
            TryDeleteAbsolute(Resolve(storageRelativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not delete {Path} from storage", storageRelativePath);
        }
    }

    public static void TryDeleteAbsolute(string absolutePath)
    {
        if (File.Exists(absolutePath))
            File.Delete(absolutePath);
    }

    public void TryDeleteDirectory(string absolutePath)
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
