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
    [HttpGet]
    public async Task<ActionResult<MonthlyRecapDto>> Get([FromQuery] string? month, CancellationToken ct) =>
        Ok(await recap.GetAsync(month, ct));

    [HttpPost("playlist")]
    public async Task<ActionResult<object>> SavePlaylist(SaveRecapPlaylistRequest request, CancellationToken ct) =>
        Ok(new { Id = await recap.SavePlaylistAsync(request, ct) });
}
