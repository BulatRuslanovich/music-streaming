namespace MusicStreaming.Application.Dtos;

public record PlaylistDto(
    Guid Id,
    string Name,
    string? Description,
    int TrackCount,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record PlaylistDetailDto(
    Guid Id,
    string Name,
    string? Description,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackDto> Tracks);

public record CreatePlaylistRequest(string Name, string? Description);
public record UpdatePlaylistRequest(string Name, string? Description);
public record AddPlaylistTrackRequest(Guid TrackId);
public record ReorderPlaylistRequest(IReadOnlyList<Guid> TrackIds);
