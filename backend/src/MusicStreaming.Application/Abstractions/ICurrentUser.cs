namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Кто выполняет текущий запрос.
///
/// <para>
/// Существует затем, чтобы слой приложения не знал ни про <c>HttpContext</c>, ни про claims:
/// личность — понятие веб-слоя, а сервисам от неё нужен только идентификатор. Единственный порт,
/// чья реализация живёт в проекте API, а не в инфраструктуре, — там же, где и её источник.
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>Идентификатор пользователя; <see cref="Guid.Empty"/>, если запрос анонимный.</summary>
    Guid Id { get; }

    bool IsAuthenticated { get; }
}
