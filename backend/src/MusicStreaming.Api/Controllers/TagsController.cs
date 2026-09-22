// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>Browsing by tag.</summary>
/// <remarks>
/// Имя тега едет параметром запроса, а не сегментом пути: теги приходят из Last.fm как есть
/// и содержат в том числе «rock/pop», а слэш внутри сегмента пришлось бы протаскивать сквозь
/// маршрутизацию закодированным.
/// </remarks>
[ApiController]
[Route("api/tags")]
public class TagsController(TagBrowseService tags) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TagDto>>> List(
        [FromQuery] int? limit, CancellationToken ct) =>
        Ok(await tags.GetTagsAsync(limit, ct));

    [HttpGet("tracks")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        [FromQuery] string? name,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await tags.GetTagTracksAsync(name, new PageRequest(page, pageSize), ct));

    [HttpGet("artists")]
    public async Task<ActionResult<IReadOnlyList<ArtistDto>>> Artists(
        [FromQuery] string? name,
        [FromQuery] int? limit,
        CancellationToken ct = default) =>
        Ok(await tags.GetTagArtistsAsync(name, limit, ct));
}
