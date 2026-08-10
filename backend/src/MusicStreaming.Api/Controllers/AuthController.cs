using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser, IWebHostEnvironment environment)
    : ControllerBase
{
    /// <summary>
    /// Cookies are marked Secure everywhere except local HTTP development, where the browser
    /// would otherwise refuse to store them.
    /// </summary>
    private bool RequireSecureCookies => !environment.IsDevelopment() || Request.IsHttps;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<UserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        AuthCookies.Write(Response, result, RequireSecureCookies);

        return Ok(result.User);
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new token pair. The old refresh token is revoked, so a
    /// stolen one stops working as soon as the real client refreshes.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<UserDto>> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[AuthCookies.RefreshTokenCookie];
        var result = await auth.RefreshAsync(token, ct);
        AuthCookies.Write(Response, result, RequireSecureCookies);

        return Ok(result.User);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[AuthCookies.RefreshTokenCookie], ct);
        AuthCookies.Clear(Response, RequireSecureCookies);

        return NoContent();
    }

    /// <summary>Used by the frontend on load to restore the session without a stored token.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) =>
        Ok(await auth.GetUserAsync(currentUser.Id, ct));
}
