namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Один исполнитель в титрах трека.
///
/// <para>
/// Существует потому, что тег вида «A feat. B» описывает не одного исполнителя, а нескольких, и
/// разбирается на список. <see cref="Position"/> хранит порядок: первый в титрах — тот же, на
/// которого ссылается сам трек, и менять их местами нельзя.
/// </para>
/// </summary>
public class TrackArtist
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public int Position { get; set; }
}
