namespace MusicStreaming.Domain.Entities;

public class ListeningStat
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset Hour { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public int PlayCount { get; set; }
    public long ListenedSeconds { get; set; }
}
