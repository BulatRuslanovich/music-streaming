using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Abstractions;


public interface IMusicStorage
{
    /// <param name="extension">Расширение с точкой, под которым файл ляжет в хранилище.</param>
    Task<StoredFile> SaveTrackAsync(Stream content, string extension, long maxBytes, CancellationToken cancellationToken = default);
    Task<string> SaveCoverAsync(Guid albumId, IReadOnlyList<ResizedImage> renditions, CancellationToken cancellationToken = default);
    Task<string> SaveArtistImageAsync(Guid artistId, byte[] webpContent, CancellationToken cancellationToken = default);
    Task<string> SavePlaylistCoverAsync(Guid playlistId, byte[] webpContent, CancellationToken cancellationToken = default);
    string CoverVariantPath(string coverPath, CoverSize size);
    void DeleteCover(string coverPath);
    string TranscodePathFor(string contentHash, AudioQuality quality);

    /// <summary>Удаляет все перекодированные варианты трека разом — вызывающему незачем знать, сколько их и какие.</summary>
    void DeleteTranscodes(string contentHash);
    FileStream? OpenRead(string storageRelativePath);
    string? ResolveExisting(string storageRelativePath);
    string ResolveForWrite(string storageRelativePath);
    void Delete(string storageRelativePath);
}

public record StoredFile(string RelativePath, long SizeBytes, string ContentHash);
