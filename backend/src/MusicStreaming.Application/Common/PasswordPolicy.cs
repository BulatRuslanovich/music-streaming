namespace MusicStreaming.Application.Common;

/// <summary>
/// Единственное место, где записано, какой пароль считается годным. Проверяют его и создание
/// учётной записи администратором, и сброс, и смена пользователем своего собственного — расходиться
/// этим трём проверкам нельзя.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>BCrypt обрезает всё после 72 байт, поэтому более длинный пароль был бы длинным только на вид.</summary>
    public const int MaxLength = 72;

    public static string Validate(string? password)
    {
        var value = password ?? string.Empty;

        return value.Length is >= MinLength and <= MaxLength
            ? value
            : throw new ValidationException($"The password must be {MinLength}-{MaxLength} characters long.");
    }
}
