namespace MusicStreaming.Application.Dtos;

/// <param name="IsActive">Деактивированная запись не может войти, но все её данные на месте.</param>
public record AdminUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsAdmin,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateUserRequest(
    string Username,
    string Password,
    string? DisplayName,
    bool IsAdmin);

public record SetUserActiveRequest(bool IsActive);

public record SetUserRoleRequest(bool IsAdmin);

public record ResetPasswordRequest(string NewPassword);
