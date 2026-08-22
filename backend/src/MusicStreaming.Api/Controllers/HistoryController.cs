// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/history")]
public class HistoryController(HistoryService history) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<HistoryEntryDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await history.GetHistoryAsync(new PageRequest(page, pageSize), ct));

    [HttpGet("recent")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Recent(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await history.GetRecentlyPlayedAsync(new PageRequest(page, pageSize), ct));


    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Record(RecordPlayRequest request, CancellationToken ct)
    {
        await history.RecordPlayAsync(request, ct);
        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await history.ClearAsync(ct);
        return NoContent();
    }
}
