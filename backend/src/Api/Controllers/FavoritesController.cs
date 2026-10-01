// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Common;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/favorites")]
public class FavoritesController(FavoriteService favorites) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await favorites.GetFavoritesAsync(new PageRequest(page, pageSize), ct));

    [HttpPost("/api/tracks/{id:guid}/favorite")]
    public async Task<IActionResult> Add(Guid id, CancellationToken ct)
    {
        await favorites.AddAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("/api/tracks/{id:guid}/favorite")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await favorites.RemoveAsync(id, ct);
        return NoContent();
    }
}
