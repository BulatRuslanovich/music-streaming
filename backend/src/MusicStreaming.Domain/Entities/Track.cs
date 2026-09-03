// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Domain.Entities;

/// <summary>Каким путём файл попал в библиотеку.</summary>
public enum IngestionSource
{
    /// <summary>Трек добавлен до того, как источник начали записывать.</summary>
    Unknown = 0,

    /// <summary>Файл прислал пользователь через форму загрузки.</summary>
    WebUpload = 1,

    /// <summary>Файл подобран автоматическим сканированием директории импорта.</summary>
    DirectoryImport = 2,
}

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
    public string? Codec { get; set; }
    public int? BitrateKbps { get; set; }
    public int? SampleRateHz { get; set; }
    public int? BitsPerSample { get; set; }
    public double ShuffleKey { get; set; } = Random.Shared.NextDouble();
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Кто прислал файл. null — у автоматического импорта: администратор, запустивший сканирование,
    /// не автор того, что лежало в папке. У треков старше этого поля тоже null.
    /// </summary>
    public Guid? AddedByUserId { get; set; }
    public User? AddedByUser { get; set; }
    public IngestionSource IngestionSource { get; set; }
    public TrackLyrics? Lyrics { get; set; }
    public TrackStats? Stats { get; set; }
    public TrackAudioFeatures? AudioFeatures { get; set; }

    /// <summary>Когда теги последний раз запрашивались у провайдера. null — ещё ни разу.</summary>
    public DateTimeOffset? TagsFetchedAt { get; set; }
    public ICollection<TrackTag> Tags { get; set; } = [];
    public ICollection<PlaylistTrack> PlaylistTracks { get; set; } = [];
    public ICollection<Favorite> Favorites { get; set; } = [];
    public ICollection<ListeningHistoryEntry> History { get; set; } = [];
}
