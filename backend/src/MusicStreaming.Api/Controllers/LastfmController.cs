using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Integrations;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Подключение Last.fm по её обычному веб-потоку: пользователь разрешает доступ на самой Last.fm,
/// а сюда возвращается с одноразовым токеном. Пароль от Last.fm Caimack не видит.
/// </summary>
[ApiController]
[Route("api/lastfm")]
public class LastfmController(
    LastfmService lastfm,
    ISecretProtector secrets,
    ICurrentUser currentUser,
    TimeProvider clock,
    IWebHostEnvironment environment,
    ILogger<LastfmController> logger) : ControllerBase
{
    private const string StateCookie = "ms_lastfm_state";
    private const string SettingsPage = "/settings";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    [HttpGet("status")]
    public async Task<ActionResult<LastfmStatusDto>> Status(CancellationToken ct) =>
        Ok(await lastfm.GetStatusAsync(ct));

    /// <summary>
    /// Отдаёт адрес страницы разрешения и запоминает, кто именно подключается.
    ///
    /// <para>
    /// Кто вернётся с токеном, определяется не куками сессии, а отдельной подписанной меткой: без
    /// неё чужую ссылку возврата можно было бы подсунуть вошедшему пользователю и привязать его
    /// учётную запись к чужому Last.fm.
    /// </para>
    /// </summary>
    [HttpPost("connect")]
    public ActionResult<LastfmConnectDto> Connect()
    {
        var callback = $"{Request.Scheme}://{Request.Host}/api/lastfm/callback";
        var url = lastfm.AuthorizeUrl(callback);

        Response.Cookies.Append(
            StateCookie,
            secrets.Protect($"{currentUser.Id}|{clock.GetUtcNow().Add(StateLifetime).ToUnixTimeSeconds()}"),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = AuthCookies.RequireSecure(Request, environment),
                SameSite = SameSiteMode.Lax,
                Path = "/api/lastfm",
                MaxAge = StateLifetime,
            });

        return Ok(new LastfmConnectDto(url));
    }

    /// <summary>
    /// Возврат с Last.fm. Отвечает переадресацией на страницу настроек, потому что сюда приходит
    /// браузер пользователя, а не код клиента.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? token, CancellationToken ct)
    {
        Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/api/lastfm" });

        if (string.IsNullOrWhiteSpace(token) || ResolveUser() is not { } userId)
            return Redirect($"{SettingsPage}?lastfm=denied");

        try
        {
            await lastfm.CompleteAsync(userId, token, ct);
            return Redirect($"{SettingsPage}?lastfm=connected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Completing the Last.fm connection failed for user {UserId}", userId);
            return Redirect($"{SettingsPage}?lastfm=failed");
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await lastfm.DisconnectAsync(ct);
        return NoContent();
    }

    /// <summary>Пользователь из подписанной метки, если она есть, цела и не просрочена.</summary>
    private Guid? ResolveUser()
    {
        if (Request.Cookies[StateCookie] is not { Length: > 0 } state)
            return null;

        if (secrets.Unprotect(state)?.Split('|') is not [var user, var expiry])
            return null;

        return Guid.TryParse(user, out var userId)
               && long.TryParse(expiry, out var unix)
               && DateTimeOffset.FromUnixTimeSeconds(unix) > clock.GetUtcNow()
            ? userId
            : null;
    }
}

/// <param name="AuthorizeUrl">Адрес страницы Last.fm, куда нужно отправить браузер.</param>
public record LastfmConnectDto(string AuthorizeUrl);
