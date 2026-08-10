namespace MusicStreaming.Application.Dtos;

public sealed record PlaylistDto(
    Guid Id,
    string Name,
    string? Description,
    int TrackCount,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PlaylistDetailDto(
    Guid Id,
    string Name,
    string? Description,
    int DurationSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackDto> Tracks);

public sealed record CreatePlaylistRequest(string Name, string? Description);

public sealed record UpdatePlaylistRequest(string Name, string? Description);

public sealed record AddPlaylistTrackRequest(Guid TrackId);

/// <summary>Full desired ordering of the playlist, sent after a drag-and-drop reorder.</summary>
public sealed record ReorderPlaylistRequest(IReadOnlyList<Guid> TrackIds);
