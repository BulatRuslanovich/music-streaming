using Microsoft.AspNetCore.Mvc;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Метаданные запущенной сборки. Отдельно от <see cref="ConfigController"/>: тот описывает
/// настройки, влияющие на поведение клиента, а здесь — сведения, нужные только разработчику.
/// </summary>
[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    // Без [AllowAnonymous]: fallback-политика уже требует аутентификации, а версию сборки незачем
    // показывать всему интернету.
    /// <summary>Версия, коммит и время сборки — по ним видно, что именно сейчас запущено.</summary>
    [HttpGet]
    public ActionResult<SystemInfoDto> Get() => Ok(BuildInfo.Current);
}

public record SystemInfoDto(string Version, string? Commit, DateTimeOffset? BuiltAt);
