// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Api.Auth;
using Microsoft.AspNetCore.Mvc;
using App.Abstractions;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(
    UserSettingsService settings,
    AuthService auth,
    ICurrentUser currentUser,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings(CancellationToken ct) =>
        Ok(UserSettingsService.ToDto(await settings.GetAsync(ct)));

    [HttpPut("settings")]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(
        UpdateUserSettingsRequest request, CancellationToken ct) =>
        Ok(await settings.UpdateAsync(request, ct));

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(request, currentUser.Id, ct);
        AuthCookies.Write(Response, result, AuthCookies.RequireSecure(Request, environment));

        return NoContent();
    }
}
