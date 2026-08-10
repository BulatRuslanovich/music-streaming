namespace MusicStreaming.Application.Dtos;

public sealed record LoginRequest(string Username, string Password);

public sealed record UserDto(Guid Id, string Username, string DisplayName);

/// <summary>
/// Result of a successful login or refresh. The API layer writes both tokens as HttpOnly
/// cookies; the access token is also returned so non-browser clients can use a bearer header.
/// </summary>
public sealed record AuthResultDto(
    UserDto User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record RecordPlayRequest(Guid TrackId, int PlaybackPosition);
