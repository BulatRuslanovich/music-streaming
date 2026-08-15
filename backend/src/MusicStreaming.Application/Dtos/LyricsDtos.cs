using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Dtos;

/// <param name="At">Смещение строки от начала трека в миллисекундах.</param>
/// <param name="Text">Текст строки.</param>
public record LyricLineDto(int At, string Text);

/// <summary>
/// Текст трека. <c>Lines</c> заполнен, только когда текст синхронизирован; в остальных случаях
/// клиент показывает <c>Plain</c> как есть.
/// </summary>
/// <param name="TrackId">Трек, которому принадлежит текст.</param>
/// <param name="Plain">Текст без меток времени.</param>
/// <param name="Lines">Строки с метками времени; пустой список — текст не синхронизирован.</param>
/// <param name="Source">Откуда взялся текст.</param>
public record LyricsDto(
    Guid TrackId,
    string Plain,
    IReadOnlyList<LyricLineDto> Lines,
    LyricsSource Source);

/// <summary>
/// Ручная правка текста администратором. Принимается и обычный текст, и LRC — формат распознаётся
/// тем же разбором, что и текст из тегов файла.
/// </summary>
public record UpdateLyricsRequest(string? Text);
