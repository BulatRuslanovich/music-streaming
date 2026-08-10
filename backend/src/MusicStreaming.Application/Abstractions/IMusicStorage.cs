namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Filesystem access for audio files and cover art. Every method takes either a
/// storage-relative path produced by this service or an entity id — never a client-supplied
/// path — and re-validates that the resolved location stays inside the storage root.
/// </summary>
public interface IMusicStorage
{
    /// <summary>
    /// Writes <paramref name="content"/> to a freshly generated, sharded location under
    /// <c>music/</c> and returns the storage-relative path plus the bytes written.
    /// Throws <see cref="Common.UploadTooLargeException"/> as soon as the copied length exceeds
    /// <paramref name="maxBytes"/>, so an oversized stream cannot fill the disk first.
    /// </summary>
    Task<StoredFile> SaveTrackAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default);

    Task<string> SaveCoverAsync(Guid albumId, byte[] content, string mimeType, CancellationToken cancellationToken = default);

    /// <summary>Opens a file for reading, or returns <c>null</c> when it is missing from disk.</summary>
    FileStream? OpenRead(string storageRelativePath);

    /// <summary>Absolute path for a storage-relative path, or <c>null</c> if it does not exist.</summary>
    string? ResolveExisting(string storageRelativePath);

    void Delete(string storageRelativePath);
}

public sealed record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
