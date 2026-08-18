namespace MusicStreaming.Domain.Entities;

public class TrackArtist
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public int Position { get; set; }
}
