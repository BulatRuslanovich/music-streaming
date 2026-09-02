// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>Каталог треков: чтение списка и правки метаданных.</summary>
/// <remarks>
/// Медиа (<see cref="TrackMediaController"/>) и загрузка (<see cref="TrackUploadsController"/>)
/// живут в своих контроллерах: маршруты те же, но за ними другие сервисы и другие заголовки.
/// </remarks>
[ApiController]
[Route("api/tracks")]
public class TracksController(CatalogService catalog, TrackEditService editor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q = null,
        [FromQuery] CatalogService.TrackSort sort = CatalogService.TrackSort.Title,
        CancellationToken ct = default) =>
        Ok(await catalog.GetTracksAsync(new PageRequest(page, pageSize), sort, q, ct));

    [HttpGet("shuffle")]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> Shuffle(
        [FromQuery] int? limit = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default) =>
        Ok(await catalog.GetShuffledTracksAsync(limit, q, ct));

    [HttpGet("{id:guid}/analysis")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrackAnalysisDto>> Analysis(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetTrackAnalysisAsync(id, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TrackDto>> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) =>
        Ok(await editor.UpdateTrackAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await editor.DeleteTrackAsync(id, ct);
        return NoContent();
    }

    [HttpPost("bulk-delete")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkDeleteResultDto>> BulkDelete(
        BulkDeleteTracksRequest request, CancellationToken ct) =>
        Ok(await editor.DeleteTracksAsync(request.Ids ?? [], ct));
}
