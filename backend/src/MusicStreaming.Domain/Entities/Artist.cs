namespace MusicStreaming.Domain.Entities;

public class Artist
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;

    /// <summary>Lower-cased, whitespace-collapsed name; carries the uniqueness constraint.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Album> Albums { get; set; } = new List<Album>();
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}
