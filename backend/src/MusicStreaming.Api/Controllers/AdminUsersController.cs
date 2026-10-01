// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
public class AdminUsersController(AdminUserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuthUserDto>>> List([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await users.GetUsersAsync(new PageRequest(page, pageSize), ct));

    [HttpPost]
    public async Task<ActionResult<AuthUserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateUserAsync(request, ct);
        return Created($"/api/admin/users/{created.Id}", created);
    }

    [HttpPut("{id:guid}/active")]
    public async Task<ActionResult<AuthUserDto>> SetActive(
        Guid id, SetUserActiveRequest request, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, request.IsActive, ct));

    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<AuthUserDto>> SetRole(
        Guid id, SetUserRoleRequest request, CancellationToken ct) =>
        Ok(await users.SetAdminAsync(id, request.IsAdmin, ct));

    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(
        Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request.NewPassword, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/sessions/revoke")]
    public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken ct)
    {
        await users.RevokeSessionsAsync(id, ct);
        return NoContent();
    }
}
