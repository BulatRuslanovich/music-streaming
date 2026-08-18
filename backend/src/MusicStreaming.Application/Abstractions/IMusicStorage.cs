using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Abstractions;

public interface IMusicStorage
{
    Task<StoredFile> SaveTrackAsync(Stream content, string extension, long maxBytes, CancellationToken cancellationToken = default);
    Task<string> SaveCoverAsync(Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken cancellationToken = default);
    Task<string> SaveArtistImageAsync(Guid artistId, byte[] webpContent, CancellationToken cancellationToken = default);
    Task<string> SavePlaylistCoverAsync(Guid playlistId, byte[] webpContent, CancellationToken cancellationToken = default);
    string CoverVariantPath(string coverPath, CoverSize size);
    void DeleteCover(string coverPath);
    string TranscodePathFor(string contentHash, AudioQuality quality);
    void DeleteTranscodes(string contentHash);
    Stream? OpenRead(string storageRelativePath);
    string? ResolveExisting(string storageRelativePath);
    string ResolveForWrite(string storageRelativePath);
    void Delete(string storageRelativePath);
}

public record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
