namespace MusicStreaming.Application.Dtos;

public record AdminUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsAdmin,
    DateTimeOffset CreatedAt);

public record CreateUserRequest(
    string Username,
    string Password,
    string? DisplayName,
    bool IsAdmin);
