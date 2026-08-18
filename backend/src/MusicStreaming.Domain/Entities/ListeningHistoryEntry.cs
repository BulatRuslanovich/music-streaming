namespace MusicStreaming.Domain.Entities;

public class ListeningHistoryEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.UtcNow;
    public int PlaybackPosition { get; set; }
}
