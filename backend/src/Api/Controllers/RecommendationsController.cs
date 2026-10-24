// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Recommendations;
using App.Recommendations.Moods;
using App.Recommendations.Radio;

namespace Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(
    RadioService radio, MoodShelf moods, RecommendationStatsService stats) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<RecommendationStatsDto>> Stats([FromQuery] int? days, CancellationToken ct) =>
        Ok(await stats.GetAsync(days ?? 30, ct));

    [HttpGet("moods")]
    public async Task<ActionResult<IReadOnlyList<MoodDto>>> Moods(CancellationToken ct) =>
        Ok(await moods.GetAsync(ct));

    [HttpPost("radio")]
    public async Task<ActionResult<RadioBatchDto>> Radio(RadioRequest request, CancellationToken ct) =>
        Ok(await radio.NextAsync(request, ct));
}
