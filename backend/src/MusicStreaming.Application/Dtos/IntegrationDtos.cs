namespace MusicStreaming.Application.Dtos;

public record LastfmStatusDto(
    bool Available,
    string? Username,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastScrobbleAt)
{
    public static readonly LastfmStatusDto Unavailable = new(false, null, null, null);
}
