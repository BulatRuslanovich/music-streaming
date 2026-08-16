using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Domain.Entities;

public class Track
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public ICollection<TrackArtist> TrackArtists { get; set; } = [];
    public Guid? AlbumId { get; set; }
    public Album? Album { get; set; }
    public Guid? GenreId { get; set; }
    public Genre? Genre { get; set; }
    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public int? Year { get; set; }
    public int DurationSeconds { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "audio/mpeg";
    public long FileSize { get; set; }
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Кодек внутри контейнера: mp3, flac, alac, aac.
    ///
    /// <para>
    /// Хранится отдельно от <see cref="MimeType"/>, потому что тот про ALAC и AAC говорит одно и то
    /// же — <c>audio/mp4</c>, — а играет браузер только второй. Различить их больше нечем.
    /// </para>
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>Средний битрейт исходника, кбит/с. <c>null</c> у всего, залитого до появления колонки.</summary>
    public int? BitrateKbps { get; set; }
    public int? SampleRateHz { get; set; }

    /// <summary>Разрядность. Есть только у форматов без потерь: у mp3 и aac её не существует.</summary>
    public int? BitsPerSample { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TrackLyrics? Lyrics { get; set; }

    /// <summary>Статистика по треку в масштабе библиотеки; <c>null</c>, пока обслуживание не посчитало её впервые.</summary>
    public TrackStats? Stats { get; set; }
    public ICollection<PlaylistTrack> PlaylistTracks { get; set; } = [];
    public ICollection<Favorite> Favorites { get; set; } = [];
    public ICollection<ListeningHistoryEntry> History { get; set; } = [];
}
