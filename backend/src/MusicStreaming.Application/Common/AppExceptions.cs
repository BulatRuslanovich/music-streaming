namespace MusicStreaming.Application.Common;

/// <summary>
/// Отказ, о котором нужно рассказать клиенту.
///
/// <para>
/// Код ответа хранится в самом исключении, а превращает его в тело
/// <c>ExceptionHandlingMiddleware</c> — единственное место, знающее про HTTP. Благодаря этому
/// сервис остаётся в своих понятиях («трека нет»), а в контроллерах нет ни одной проверки вида
/// «не нашли — вернуть 404».
/// </para>
///
/// <para>
/// Исключения, а не <c>Result</c>: отказ здесь почти всегда просто поднимается наверх и становится
/// кодом ответа, а <c>Result</c> пришлось бы протаскивать через все промежуточные сигнатуры ради
/// единственного места, где он нужен (см. docs/backend/adr/0005-exceptions-instead-of-result.md).
/// </para>
/// </summary>
public abstract class AppException(string message) : Exception(message)
{
    /// <summary>Код ответа HTTP, соответствующий этому отказу.</summary>
    public abstract int StatusCode { get; }
}

/// <summary>
/// Объекта нет — или он есть, но принадлежит другому пользователю. Второй случай намеренно
/// отвечает так же: 403 на чужой плейлист подтверждал бы, что такой плейлист существует.
/// </summary>
public class NotFoundException(string message = "The requested resource was not found.")
    : AppException(message)
{
    public override int StatusCode => 404;
}

/// <summary>Входные данные не проходят проверку.</summary>
public class ValidationException(string message) : AppException(message)
{
    public override int StatusCode => 400;
}

/// <summary>Запрос сам по себе верен, но состояние ему противоречит: занятое имя, дубликат, уже загруженный файл.</summary>
public class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => 409;
}

/// <summary>Кто выполняет запрос — неизвестно: токена нет, он истёк или отозван.</summary>
public class AuthenticationException(string message = "Invalid credentials.")
    : AppException(message)
{
    public override int StatusCode => 401;
}

/// <summary>Пользователь известен, но прав не хватает. Сюда же неверный пароль при входе — чтобы ответ не отличал «нет такого имени» от «пароль не тот».</summary>
public class ForbiddenException(string message) : AppException(message)
{
    public override int StatusCode => 403;
}

/// <summary>Файл больше допустимого. Внешние рубежи (прокси и Kestrel) обрывают такой запрос грубо; это — вежливый ответ для того, что до сервиса всё же дошло.</summary>
public class UploadTooLargeException(long maxBytes)
    : AppException($"The file exceeds the {maxBytes / (1024 * 1024)} MB upload limit.")
{
    public override int StatusCode => 413;
}
