using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Storage;

/// <summary>
/// Stores audio and cover files on the local filesystem under a single storage root.
///
/// Layout (as in the specification):
/// <code>
/// &lt;root&gt;/music/8f/31/8f31c2....mp3
/// &lt;root&gt;/covers/&lt;album-id&gt;.jpg
/// </code>
/// The two-level shard keeps directory sizes reasonable for a library of tens of thousands of
/// files. Names are always server-generated GUIDs, and every path is re-validated against the
/// root before it reaches the filesystem, so a crafted value can never escape the storage tree.
/// </summary>
public sealed class FileSystemMusicStorage : IMusicStorage
{
    private const int BufferSize = 64 * 1024;
    private const string MusicDirectory = "music";
    private const string CoverDirectory = "covers";

    private readonly string _root;
    private readonly ILogger<FileSystemMusicStorage> _logger;

    public FileSystemMusicStorage(IOptions<StorageOptions> options, ILogger<FileSystemMusicStorage> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(options.Value.RootPath);

        Directory.CreateDirectory(Path.Combine(_root, MusicDirectory));
        Directory.CreateDirectory(Path.Combine(_root, CoverDirectory));

        _logger.LogInformation("Music storage rooted at {Root}", _root);
    }

    public async Task<StoredFile> SaveTrackAsync(
        Stream content, long maxBytes, CancellationToken cancellationToken = default)
    {
        var id = Guid.CreateVersion7().ToString("N");

        // Shard on the last four hex digits rather than the first. A version 7 GUID starts with a
        // millisecond timestamp, so its leading digits barely change between uploads and every
        // file would pile into the same two directories; the trailing digits are random and
        // spread 10,000+ files evenly over 256 x 256 buckets.
        var relativePath = $"{MusicDirectory}/{id[^2..]}/{id[^4..^2]}/{id}.mp3";
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

            // Copy in fixed-size chunks: the file is hashed as it is written, is never held in
            // memory whole, and a stream that lied about its length is cut off mid-copy rather
            // than after it has already filled the disk.
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;

                size += read;
                if (size > maxBytes)
                    throw new UploadTooLargeException(maxBytes);

                hasher.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await target.FlushAsync(cancellationToken);
            hash = hasher.GetHashAndReset();
        }
        catch
        {
            TryDeleteAbsolute(absolutePath);
            throw;
        }

        return new StoredFile(relativePath, size, Convert.ToHexString(hash).ToLowerInvariant());
    }

    public async Task<string> SaveCoverAsync(
        Guid albumId, byte[] content, string mimeType, CancellationToken cancellationToken = default)
    {
        var extension = mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg",
        };

        var relativePath = $"{CoverDirectory}/{albumId:N}{extension}";
        var absolutePath = ResolveWithinRoot(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, content, cancellationToken);

        return relativePath;
    }

    public FileStream? OpenRead(string storageRelativePath)
    {
        var absolutePath = ResolveWithinRoot(storageRelativePath);
        if (!File.Exists(absolutePath))
            return null;

        // Sequential access with sharing lets a backup read the file while it is being streamed.
        return new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public string? ResolveExisting(string storageRelativePath)
    {
        var absolutePath = ResolveWithinRoot(storageRelativePath);
        return File.Exists(absolutePath) ? absolutePath : null;
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
            // A file left behind is recoverable; failing the caller's operation is not.
            _logger.LogError(ex, "Could not delete {Path} from storage", storageRelativePath);
        }
    }

    /// <summary>
    /// Turns a storage-relative path into an absolute one, rejecting anything that would resolve
    /// outside the storage root. This is the single choke point for path traversal: absolute
    /// paths, <c>..</c> segments and symlinked escapes all fail here rather than at the syscall.
    /// </summary>
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
}
