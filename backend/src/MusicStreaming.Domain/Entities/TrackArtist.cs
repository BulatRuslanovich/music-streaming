namespace MusicStreaming.Domain.Entities;

/// <summary>
/// One artist credited on one track. A track keeps every performer its tag named, while
/// <see cref="Track.ArtistId"/> stays the primary credit — the one the track is filed under.
/// </summary>
public class TrackArtist
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }

    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }

    /// <summary>Zero-based credit order as it appeared in the tag; position 0 is the primary artist.</summary>
    public int Position { get; set; }
}
