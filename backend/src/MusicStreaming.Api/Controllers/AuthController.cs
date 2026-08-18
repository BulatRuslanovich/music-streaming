using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Вход, продление и выход.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, ICurrentUser currentUser, IWebHostEnvironment environment)
    : ControllerBase
{
    private bool RequireSecureCookies => AuthCookies.RequireSecure(Request, environment);

    /// <summary>
    /// Вход по имени и паролю.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        AuthCookies.Write(Response, result, RequireSecureCookies);

        return Ok(result.User);
    }

    /// <summary>
    /// Меняет refresh-токен на новую пару.
    ///
    /// <para>
    /// Анонимна намеренно: её зовут именно тогда, когда токен доступа уже истёк. Сам refresh-токен
    /// берётся из куки, а не из тела, — у неё свой путь <c>/api/auth</c>, так что с обычными
    /// запросами она не ездит.
    /// </para>
    ///
    /// <para>
    /// Предъявленный токен отзывается, а повторное предъявление уже отозванного считается кражей и
    /// гасит все сессии пользователя — кроме случая, когда это две вкладки, продлевающиеся
    /// одновременно (см. <c>AuthService.RefreshAsync</c>).
    /// </para>
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[AuthCookies.RefreshTokenCookie];
        var result = await auth.RefreshAsync(token, ct);
        AuthCookies.Write(Response, result, RequireSecureCookies);

        return Ok(result.User);
    }

    /// <summary>
    /// Завершает сессию: отзывает refresh-токен и удаляет куки.
    ///
    /// <para>
    /// Анонимна и всегда отвечает 204: выйти должно получаться и с протухшим токеном, и вовсе без
    /// него — иначе единственным способом избавиться от испорченной сессии осталась бы чистка кук
    /// руками.
    /// </para>
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[AuthCookies.RefreshTokenCookie], ct);
        AuthCookies.Clear(Response, RequireSecureCookies);

        return NoContent();
    }

    /// <summary>Текущий пользователь — то, что клиент показывает в профиле и по чему решает, показывать ли админские разделы.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) =>
        Ok(await auth.GetUserAsync(currentUser.Id, ct));
}
