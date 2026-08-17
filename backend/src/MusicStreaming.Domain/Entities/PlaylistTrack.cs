namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Трек внутри плейлиста.
///
/// <para>
/// Пара «плейлист и трек» уникальна на уровне базы. Это не украшение схемы: позиция раньше
/// вычислялась запросом <c>MAX(position) + 1</c>, и два одновременных добавления — двойной клик или
/// два устройства — читали одно значение и вставляли обе строки. Ответить на «уже есть?» без гонки
/// способна только база.
/// </para>
/// </summary>
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
