// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;


[ApiController]
[Route("api/favorites")]
public class FavoritesController(FavoriteService favorites) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await favorites.GetFavoritesAsync(new PageRequest(page, pageSize), ct));

    // Маршрут остаётся под /api/tracks — избранное относится к треку, а не к списку. Здесь он
    // потому, что за ним тот же сервис, что и за списком выше.
    [HttpPost("/api/tracks/{id:guid}/favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid id, CancellationToken ct)
    {
        await favorites.AddAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("/api/tracks/{id:guid}/favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await favorites.RemoveAsync(id, ct);
        return NoContent();
    }
}
