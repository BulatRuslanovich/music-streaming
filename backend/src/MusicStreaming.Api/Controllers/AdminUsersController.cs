// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Управление учётными записями.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
public class AdminUsersController(AdminUserService users) : ControllerBase
{
    /// <summary>Все учётные записи, включая деактивированные.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await users.GetUsersAsync(new PageRequest(page, pageSize), ct));

    /// <summary>Заводит учётную запись. Самостоятельной регистрации в приложении нет — это единственный способ.</summary>
    [HttpPost]
    [ProducesResponseType<AdminUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateUserAsync(request, ct);
        return Created($"/api/admin/users/{created.Id}", created);
    }

    /// <summary>Включает или выключает учётную запись; выключение обрывает и все её сессии.</summary>
    [HttpPut("{id:guid}/active")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> SetActive(
        Guid id, SetUserActiveRequest request, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, request.IsActive, ct));

    /// <summary>
    /// Выдаёт или снимает права администратора.
    /// 
    /// </summary>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> SetRole(
        Guid id, SetUserRoleRequest request, CancellationToken ct) =>
        Ok(await users.SetAdminAsync(id, request.IsAdmin, ct));

    /// <summary>Задаёт пользователю новый пароль. Прежние сессии при этом отзываются.</summary>
    [HttpPost("{id:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(
        Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request.NewPassword, ct);
        return NoContent();
    }

    /// <summary>Отзывает все refresh-токены пользователя — «выйти со всех устройств».</summary>
    [HttpPost("{id:guid}/sessions/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken ct)
    {
        await users.RevokeSessionsAsync(id, ct);
        return NoContent();
    }
}
