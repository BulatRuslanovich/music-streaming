namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Хеширование паролей.
///
/// <para>
/// Алгоритм намеренно медленный, и это его главное свойство, а не недостаток: именно оно делает
/// перебор дорогим. Поэтому проверка пароля выполняется даже тогда, когда пользователя с таким
/// именем нет, — иначе время ответа выдавало бы существование учётной записи (см.
/// <c>AuthService.LoginAsync</c>).
/// </para>
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
