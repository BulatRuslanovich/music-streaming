using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Управление учётными записями. Удаления здесь нет намеренно — см. <see cref="AdminUserService"/>.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AppPolicies.Admin)]
public class AdminUsersController(AdminUserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await users.GetUsersAsync(new PageRequest(page, pageSize), ct));

    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateUserAsync(request, ct);
        return Created($"/api/admin/users/{created.Id}", created);
    }

    /// <summary>Включает или выключает учётную запись; выключение обрывает и все её сессии.</summary>
    [HttpPut("{id:guid}/active")]
    public async Task<ActionResult<AdminUserDto>> SetActive(
        Guid id, SetUserActiveRequest request, CancellationToken ct) =>
        Ok(await users.SetActiveAsync(id, request.IsActive, ct));

    [HttpPut("{id:guid}/role")]
    public async Task<ActionResult<AdminUserDto>> SetRole(
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
