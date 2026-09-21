// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(
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
}
