namespace MusicStreaming.Application.Dtos;

public sealed record LoginRequest(string Username, string Password);

public sealed record UserDto(Guid Id, string Username, string DisplayName, bool IsAdmin);


public sealed record AuthResultDto(
    UserDto User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record RecordPlayRequest(Guid TrackId, int PlaybackPosition);
