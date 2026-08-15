using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Abstractions;

public interface IAudioMetadataReader
{
    AudioMetadata? Read(string absolutePath);
}

/// <param name="Lyrics">Несинхронизированный текст (кадр USLT). Часто содержит LRC — разбирает его <c>LyricsText</c>, а не читатель тегов.</param>
/// <param name="SyncedLyrics">Строки из кадра SYLT, где время уже лежит отдельным полем; пусто, если кадра нет.</param>
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
    string? CoverMimeType,
    string? Lyrics,
    IReadOnlyList<LyricLine> SyncedLyrics);
