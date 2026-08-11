namespace MusicStreaming.Application.Abstractions;


public interface IMusicStorage
{
    Task<StoredFile> SaveTrackAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default);
    Task<string> SaveCoverAsync(Guid albumId, byte[] content, string mimeType, CancellationToken cancellationToken = default);
    Task<string> SaveArtistImageAsync(Guid artistId, byte[] webpContent, CancellationToken cancellationToken = default);
    FileStream? OpenRead(string storageRelativePath);
    string? ResolveExisting(string storageRelativePath);
    void Delete(string storageRelativePath);
}

public record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
