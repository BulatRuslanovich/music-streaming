namespace MusicStreaming.Domain.Entities;

/// <summary>Одна строка синхронизированного текста.</summary>
/// <param name="At">Смещение от начала трека в миллисекундах.</param>
/// <param name="Text">Текст строки; пустая строка — законная пауза между куплетами.</param>
public record LyricLine(int At, string Text);

/// <summary>Откуда взялся текст — влияет только на то, что показать в интерфейсе.</summary>
public enum LyricsSource
{
    /// <summary>Вычитан из тегов самого файла при загрузке.</summary>
    Embedded = 0,

    /// <summary>Введён администратором вручную.</summary>
    Manual = 1,
}

/// <summary>
/// Текст одного трека.
///
/// <para>
/// Отдельная таблица, а не колонки в <see cref="Track"/>: текст — это килобайты, которые не нужны
/// ни одному списку, а <see cref="Track"/> грузится целиком в путях правки и удаления. Здесь же у
/// текста появляется собственный срок жизни: его можно переписать вручную, не трогая сам трек, и
/// отличить «текста нет» от «текст ещё не искали».
/// </para>
/// </summary>
public class TrackLyrics
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>Простой текст. Заполнен всегда, когда есть хоть что-то: для синхронизированного текста это его же строки без меток времени.</summary>
    public string Plain { get; set; } = string.Empty;

    /// <summary>Строки с метками времени, если они известны; пустой список — текст только простой.</summary>
    public IReadOnlyList<LyricLine> Synced { get; set; } = [];

    public LyricsSource Source { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsSynced => Synced.Count > 0;
}
