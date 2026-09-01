// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>Тексты песен трека: чтение всем, правка администратору.</summary>
[ApiController]
[Route("api/tracks/{id:guid}/lyrics")]
public class TrackLyricsController(LyricsService lyrics) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LyricsDto>> Get(Guid id, CancellationToken ct) =>
        await lyrics.GetAsync(id, ct) is { } found ? Ok(found) : NoContent();

    [HttpPut]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LyricsDto>> Replace(
        Guid id, UpdateLyricsRequest request, CancellationToken ct) =>
        await lyrics.ReplaceAsync(id, request.Text, ct) is { } saved ? Ok(saved) : NoContent();
}
