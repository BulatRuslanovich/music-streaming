namespace MusicStreaming.Domain.Entities;

public class Playlist
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PlaylistTrack> Tracks { get; set; } = new List<PlaylistTrack>();
}
