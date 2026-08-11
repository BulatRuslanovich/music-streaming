namespace MusicStreaming.Application.Dtos;

/// <summary>A user row as the admin list shows it; richer than the session's own UserDto.</summary>
public sealed record AdminUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsAdmin,
    DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string? DisplayName,
    bool IsAdmin);
