using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Security;

/// <summary>
/// Хеширование паролей алгоритмом BCrypt.
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// Стоимость хеширования: каждая единица удваивает работу.
    ///
    /// <para>
    /// Двенадцать — это порядка сотни миллисекунд на нынешнем железе: незаметно человеку, который
    /// входит раз в месяц, и разорительно для перебора. Значение записано здесь, а не в настройках,
    /// намеренно: оно хранится внутри самого хеша, поэтому уже выданные пароли продолжают
    /// проверяться со своей прежней стоимостью, и менять его можно, ничего не ломая.
    /// </para>
    /// </summary>
    private const int WorkFactor = 12;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>
    /// Сверяет пароль с хешем.
    ///
    /// <para>
    /// Испорченный хеш — это «не совпало», а не исключение: сюда попадает и заведомо фиктивный
    /// хеш, с которым сверяется несуществующий пользователь, чтобы время ответа не выдавало
    /// существование учётной записи.
    /// </para>
    /// </summary>
    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
