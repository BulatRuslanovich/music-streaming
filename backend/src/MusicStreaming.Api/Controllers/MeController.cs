// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(
    UserSettingsService settings,
    StatisticsService statistics,
    AuthService auth,
    ICurrentUser currentUser,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings(CancellationToken ct) =>
        Ok(UserSettingsService.ToDto(await settings.GetAsync(ct)));

    [HttpPut("settings")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(
        UpdateUserSettingsRequest request, CancellationToken ct) =>
        Ok(await settings.UpdateAsync(request, ct));

    [HttpGet("statistics")]
    public async Task<ActionResult<StatisticsDto>> Statistics(
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Month, CancellationToken ct = default) =>
        Ok(await statistics.GetAsync(period, ct));

    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(request, currentUser.Id, ct);
        AuthCookies.Write(Response, result, AuthCookies.RequireSecure(Request, environment));

        return NoContent();
    }
}
