namespace MusicStreaming.Application.Dtos;

public record LoginRequest(string Username, string Password);

public record UserDto(Guid Id, string Username, string DisplayName, bool IsAdmin);

public record AuthResultDto(
    UserDto User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public record RecordPlayRequest(Guid TrackId, int PlaybackPosition);
