namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Исполнитель.
///
/// <para>
/// Заводится не человеком, а загрузкой трека — из тега. Поэтому опознаётся по
/// <see cref="NormalizedName"/>, на котором стоит уникальный индекс: иначе «The Beatles» из одного
/// файла и «the  beatles» из другого стали бы двумя разными исполнителями, и альбом разъехался бы
/// между ними.
/// </para>
/// </summary>
public class Artist
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Album> Albums { get; set; } = [];
    public ICollection<Track> Tracks { get; set; } = [];
    public ICollection<TrackArtist> TrackCredits { get; set; } = [];
}
