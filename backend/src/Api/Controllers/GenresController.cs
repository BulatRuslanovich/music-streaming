// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Common;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/genres")]
public class GenresController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> List(CancellationToken ct) =>
        Ok(await catalog.GetGenresAsync(ct));

    [HttpGet("{id:guid}/tracks")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await catalog.GetGenreTracksAsync(id, new PageRequest(page, pageSize), ct));
}
