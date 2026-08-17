namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Жанр. Не справочник, а то, что встретилось в тегах: заводится с первым таким треком и исчезает с
/// последним. Отсюда же отсутствие иерархии — в теге лежит строка, а не место в классификации.
/// </summary>
public class Genre
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Track> Tracks { get; set; } = [];
}
