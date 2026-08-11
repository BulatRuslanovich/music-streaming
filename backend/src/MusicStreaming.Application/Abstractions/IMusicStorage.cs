namespace MusicStreaming.Application.Abstractions;


public interface IMusicStorage
{

    Task<StoredFile> SaveTrackAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default);

    Task<string> SaveCoverAsync(Guid albumId, byte[] content, string mimeType, CancellationToken cancellationToken = default);

    Task<string> SaveArtistImageAsync(Guid artistId, byte[] webpContent, CancellationToken cancellationToken = default);

    /// <summary>Opens a file for reading, or returns <c>null</c> when it is missing from disk.</summary>
    FileStream? OpenRead(string storageRelativePath);

    /// <summary>Absolute path for a storage-relative path, or <c>null</c> if it does not exist.</summary>
    string? ResolveExisting(string storageRelativePath);

    void Delete(string storageRelativePath);
}

public sealed record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
