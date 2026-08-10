namespace MusicStreaming.Domain.Entities;

public class Album
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;

    /// <summary>The album artist, which for compilations differs from the per-track artist.</summary>
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }

    public int? Year { get; set; }

    /// <summary>Storage-relative path of the extracted cover, e.g. "covers/&lt;id&gt;.jpg".</summary>
    public string? CoverPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}
