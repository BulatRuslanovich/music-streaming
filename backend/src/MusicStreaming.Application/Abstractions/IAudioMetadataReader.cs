namespace MusicStreaming.Application.Abstractions;

public interface IAudioMetadataReader
{
    /// <summary>
    /// Reads ID3 tags and stream properties. Returns <c>null</c> when the file cannot be parsed
    /// as MP3 at all, which doubles as the file-integrity check for uploads.
    /// </summary>
    AudioMetadata? Read(string absolutePath);
}

public sealed record AudioMetadata(
    string? Title,
    string? Artist,
    string? AlbumArtist,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    int DurationSeconds,
    byte[]? CoverData,
    string? CoverMimeType);
