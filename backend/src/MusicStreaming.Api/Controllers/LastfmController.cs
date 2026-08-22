// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services.Integrations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/lastfm")]
public class LastfmController(
    LastfmService lastfm,
    ISecretProtector secrets,
    ICurrentUser currentUser,
    TimeProvider clock,
    IWebHostEnvironment environment,
    IOptions<LastfmOptions> lastfmOptions,
    ILogger<LastfmController> logger) : ControllerBase
{
    private const string StateCookie = "ms_lastfm_state";
    private const string SettingsPage = "/settings";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    [HttpGet("status")]
    public async Task<ActionResult<LastfmStatusDto>> Status(CancellationToken ct) =>
        Ok(await lastfm.GetStatusAsync(ct));

    [HttpPost("connect")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<LastfmConnectDto> Connect()
    {
        var origin = Text.TrimToNull(lastfmOptions.Value.PublicUrl)?.TrimEnd('/')
                     ?? $"{Request.Scheme}://{Request.Host}";

        var url = lastfm.AuthorizeUrl($"{origin}/api/lastfm/callback");

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

    [HttpGet("callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await lastfm.DisconnectAsync(ct);
        return NoContent();
    }

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


public record LastfmConnectDto(string AuthorizeUrl);
