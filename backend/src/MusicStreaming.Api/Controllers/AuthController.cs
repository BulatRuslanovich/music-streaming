// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Startup;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, ICurrentUser currentUser, IWebHostEnvironment environment) : ControllerBase
{
    private bool RequireSecureCookies => AuthCookies.RequireSecure(Request, environment);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RequestPipelineSetup.LoginPolicy)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthUserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        AuthCookies.Write(Response, result, RequireSecureCookies);

        return Ok(result.User);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthUserDto>> Refresh(CancellationToken ct)
    {
        var token = Request.Cookies[AuthCookies.RefreshTokenCookie];
        var result = await auth.RefreshAsync(token, ct);
        AuthCookies.Write(Response, result, RequireSecureCookies);

        return Ok(result.User);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[AuthCookies.RefreshTokenCookie], ct);
        AuthCookies.Clear(Response, RequireSecureCookies);

        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct) =>
        Ok(await auth.GetUserAsync(currentUser.Id, ct));
}
