namespace MusicStreaming.Application.Abstractions;

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
