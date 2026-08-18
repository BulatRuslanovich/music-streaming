namespace MusicStreaming.Domain.Entities;

public class Favorite
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
