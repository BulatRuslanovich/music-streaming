// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Startup;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
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

        try
        {
            var result = await auth.RefreshAsync(token, ct);
            AuthCookies.Write(Response, result, RequireSecureCookies);

            return Ok(result.User);
        }
        catch (AuthenticationException ex)
        {
            // Сессия мертва — куки обязаны уйти вместе с ней. Подсказка ms_session живёт столько
            // же, сколько refresh-токен, то есть переживает его отзыв: пока она на месте,
            // middleware считает слушателя вошедшим и заворачивает его с /login обратно на
            // страницу, где всё отвечает 401. Выйти из этой петли можно было только инкогнито.
            //
            // Отвечаем здесь, а не броском: ExceptionHandlingMiddleware вызывает Response.Clear(),
            // и Set-Cookie с удалением до браузера бы не доехал.
            AuthCookies.Clear(Response, RequireSecureCookies);

            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: ex.Message);
        }
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
