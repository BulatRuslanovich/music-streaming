// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/me/recap")]
public class RecapController(MonthlyRecapService recap) : ControllerBase
{
    /// <summary>Итоги прошлого месяца. Вне окна первых семи дней их нет — 404.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonthlyRecapDto>> Get(CancellationToken ct) =>
        Ok(await recap.GetAsync(ct));

    [HttpPost("playlist")]
    public async Task<ActionResult<object>> SavePlaylist(SaveRecapPlaylistRequest request, CancellationToken ct) =>
        Ok(new { Id = await recap.SavePlaylistAsync(request, ct) });
}
