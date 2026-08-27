// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(
    RecommendationService recommendations,
    RecommendationFeedbackService feedback,
    RadioService radio,
    DjSessionService dj) : ControllerBase
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

    [HttpGet("feedback")]
    public async Task<ActionResult<IReadOnlyList<RecommendationSuppressionDto>>> Feedback(
        CancellationToken ct) =>
        Ok(await feedback.GetSuppressionsAsync(ct));

    [HttpPost("feedback")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecommendationSuppressionDto>> Suppress(
        RecommendationFeedbackRequest request, CancellationToken ct) =>
        Ok(await feedback.SuppressAsync(request, ct));

    [HttpDelete("feedback/{target}/{targetId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(
        SuppressionTarget target, Guid targetId, CancellationToken ct)
    {
        await feedback.RestoreAsync(target, targetId, ct);
        return NoContent();
    }

    private bool IncludeScores(bool debug) => debug && User.IsInRole("Admin");
}
