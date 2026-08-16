using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Abstractions;

public interface IAudioMetadataReader
{
    /// <param name="tagLibMimeType">
    /// Каким разборщиком читать. Передаётся явно, а не выводится из расширения на диске: имени
    /// файла здесь не доверяют.
    /// </param>
    AudioMetadata? Read(string absolutePath, string tagLibMimeType);
}

/// <param name="Lyrics">Несинхронизированный текст (кадр USLT у MP3, VORBIS_COMMENT у FLAC, атом ©lyr у M4A). Часто содержит LRC — разбирает его <c>LyricsText</c>, а не читатель тегов.</param>
/// <param name="SyncedLyrics">Строки из кадра SYLT, где время уже лежит отдельным полем; пусто, если кадра нет — а у FLAC и M4A его не бывает вовсе.</param>
/// <param name="Codec">Кодек внутри контейнера: mp3, flac, alac, aac. <c>null</c>, если распознать не удалось.</param>
/// <param name="BitsPerSample">Разрядность; есть только у форматов без потерь.</param>
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
    IReadOnlyList<LyricLine> SyncedLyrics,
    string? Codec,
    int? BitrateKbps,
    int? SampleRateHz,
    int? BitsPerSample);
