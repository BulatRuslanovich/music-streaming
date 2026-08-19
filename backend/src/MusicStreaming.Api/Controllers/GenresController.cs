// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Жанры библиотеки.
/// </summary>
[ApiController]
[Route("api/genres")]
public class GenresController(CatalogService catalog) : ControllerBase
{
    /// <summary>Все жанры.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> List(CancellationToken ct) =>
        Ok(await catalog.GetGenresAsync(ct));

    /// <summary>Треки жанра.</summary>
    [HttpGet("{id:guid}/tracks")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await catalog.GetGenreTracksAsync(id, new PageRequest(page, pageSize), ct));
}
