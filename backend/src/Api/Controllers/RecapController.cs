// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/recap")]
public class RecapController(RecapService recap) : ControllerBase
{
    // Итоги прошлого месяца. Вне первой недели месяца их нет — это обычное состояние, а не ошибка: 204.
    [HttpGet]
    public async Task<ActionResult<RecapDto>> Current(CancellationToken ct) =>
        await recap.CurrentAsync(ct) is { } current ? Ok(current) : NoContent();
}
