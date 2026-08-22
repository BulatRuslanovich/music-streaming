// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(
    RecommendationService recommendations, RadioService radio, DjSessionService dj) : ControllerBase
{
    [HttpPost("dj")]
    public async Task<ActionResult<DjBatchDto>> Dj(DjRequest request, CancellationToken ct) =>
        Ok(await dj.GenerateAsync(request, ct));

    [HttpPost("radio")]
    public async Task<ActionResult<RadioBatchDto>> Radio(RadioRequest request, CancellationToken ct) =>
        Ok(await radio.NextAsync(request, ct));

    [HttpGet("home")]
    public async Task<ActionResult<RecommendationHomeDto>> Home(
        [FromQuery] int sectionSize = 12,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetHomeAsync(sectionSize, IncludeScores(debug), ct));

    [HttpGet("tracks")]
    public async Task<ActionResult<PagedResult<RecommendedTrackDto>>> Tracks(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetTracksAsync(new PageRequest(page, pageSize), IncludeScores(debug), ct));

    [HttpGet("artists")]
    public async Task<ActionResult<IReadOnlyList<ArtistDto>>> Artists(
        [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetArtistsAsync(limit, ct));

    [HttpGet("albums")]
    public async Task<ActionResult<IReadOnlyList<AlbumDto>>> Albums(
        [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetAlbumsAsync(limit, ct));

    [HttpGet("similar/{trackId:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RecommendedTrackDto>>> Similar(
        Guid trackId,
        [FromQuery] int limit = 20,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetSimilarAsync(trackId, limit, IncludeScores(debug), ct));

    private bool IncludeScores(bool debug) => debug && User.IsInRole("Admin");
}
