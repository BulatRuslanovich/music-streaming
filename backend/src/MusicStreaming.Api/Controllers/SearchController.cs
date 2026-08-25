// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Startup;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/search")]
[EnableRateLimiting(RequestPipelineSetup.SearchPolicy)]
public class SearchController(SearchService search) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await search.SearchAsync(q, limit, ct));

    [HttpGet("tracks")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await search.SearchTracksAsync(q, new PageRequest(page, pageSize), ct));

    [HttpGet("albums")]
    public async Task<ActionResult<PagedResult<AlbumDto>>> Albums(
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await search.SearchAlbumsAsync(q, new PageRequest(page, pageSize), ct));

    [HttpGet("artists")]
    public async Task<ActionResult<PagedResult<ArtistDto>>> Artists(
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await search.SearchArtistsAsync(q, new PageRequest(page, pageSize), ct));

    [HttpGet("genres")]
    public async Task<ActionResult<PagedResult<GenreDto>>> Genres(
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await search.SearchGenresAsync(q, new PageRequest(page, pageSize), ct));
}
