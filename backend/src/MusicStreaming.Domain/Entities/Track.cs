namespace MusicStreaming.Domain.Entities;

public class Track
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = string.Empty;

    /// <summary>Lower-cased title carrying the trigram index that backs search.</summary>
    public string NormalizedTitle { get; set; } = string.Empty;

    /// <summary>The primary credit: the first artist named by the tag, and the one the track is filed under.</summary>
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }

    /// <summary>Every credited artist, primary included, in tag order.</summary>
    public ICollection<TrackArtist> TrackArtists { get; set; } = new List<TrackArtist>();

    public Guid? AlbumId { get; set; }
    public Album? Album { get; set; }

    public Guid? GenreId { get; set; }
    public Genre? Genre { get; set; }

    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public int? Year { get; set; }

    /// <summary>Playback length in seconds, read from the MP3 header.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Path relative to the storage root, e.g. "music/8f/31/8f31c2....mp3".
    /// Always server-generated: a client-supplied path must never reach the filesystem.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "audio/mpeg";
    public long FileSize { get; set; }

    /// <summary>SHA-256 of the file contents, used to reject re-uploads of the same track.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PlaylistTrack> PlaylistTracks { get; set; } = new List<PlaylistTrack>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    public ICollection<ListeningHistoryEntry> History { get; set; } = new List<ListeningHistoryEntry>();
}
