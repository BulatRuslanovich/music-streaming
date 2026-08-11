namespace MusicStreaming.Application.Abstractions;

public interface IAudioMetadataReader
{
    AudioMetadata? Read(string absolutePath);
}

public record AudioMetadata(
    string? Title,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> AlbumArtists,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    int DurationSeconds,
    byte[]? CoverData,
    string? CoverMimeType);
