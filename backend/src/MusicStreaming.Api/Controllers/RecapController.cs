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
    /// <summary>Last month in review. Outside the first seven days of a month it does not exist — 404.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonthlyRecapDto>> Get(CancellationToken ct) =>
        Ok(await recap.GetAsync(ct));

    [HttpPost("playlist")]
    public async Task<ActionResult<object>> SavePlaylist(SaveRecapPlaylistRequest request, CancellationToken ct) =>
        Ok(new { Id = await recap.SavePlaylistAsync(request, ct) });
}
