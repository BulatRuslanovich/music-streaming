// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/tracks/{id:guid}/lyrics")]
public class TrackLyricsController(LyricsService lyrics) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LyricsDto>> Get(Guid id, CancellationToken ct) =>
        await lyrics.GetAsync(id, ct) is { } found ? Ok(found) : NoContent();

    [HttpPut]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<LyricsDto>> Replace(
        Guid id, UpdateLyricsRequest request, CancellationToken ct) =>
        await lyrics.ReplaceAsync(id, request.Text, ct) is { } saved ? Ok(saved) : NoContent();
}
