namespace MusicStreaming.Domain.Entities;

public class PlaylistTrack
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int Position { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
