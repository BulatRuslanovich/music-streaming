namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Имена claims в токене доступа.
///
/// <para>
/// Собраны в одном месте, потому что их знают трое: тот, кто выпускает токен, тот, кто его
/// проверяет, и тот, кто читает из него личность. Разъехавшаяся строка здесь не ломает сборку —
/// она просто оставляет всех анонимными.
/// </para>
/// </summary>
public static class AppClaims
{
    public const string UserId = "sub";
    public const string Username = "username";
    public const string Role = "role";
}

public static class AppRoles
{
    public const string Admin = "Admin";
}

public static class AppPolicies
{
    public const string Admin = "Admin";
}
