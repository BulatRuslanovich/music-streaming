namespace MusicStreaming.Application.Dtos;

/// <param name="Available">Настроен ли Last.fm на этом сервере; если нет, подключаться некуда и кнопку показывать не надо.</param>
/// <param name="Username">Подключённая учётная запись Last.fm; <c>null</c> — не подключена.</param>
/// <param name="ConnectedAt">Когда подключили.</param>
/// <param name="LastScrobbleAt">Когда последнее прослушивание действительно доехало — единственный честный признак живой связи.</param>
public record LastfmStatusDto(
    bool Available,
    string? Username,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastScrobbleAt)
{
    public static readonly LastfmStatusDto Unavailable = new(false, null, null, null);
}
