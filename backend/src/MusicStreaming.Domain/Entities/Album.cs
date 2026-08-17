namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Альбом. Как и исполнитель, появляется из тегов при загрузке. Опознаётся парой «исполнитель и
/// нормализованное название», поэтому одноимённые сборники разных исполнителей не сливаются.
/// </summary>
public class Album
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public int? Year { get; set; }
    public string? CoverPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Track> Tracks { get; set; } = [];
}
