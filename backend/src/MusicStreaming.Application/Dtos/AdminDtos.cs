namespace MusicStreaming.Application.Dtos;

public record AdminUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    bool IsAdmin,

    /// <summary>Деактивированная запись не может войти, но все её данные на месте.</summary>
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
