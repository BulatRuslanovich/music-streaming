// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Recommendations.Moods;
using App.Recommendations.Radio;

namespace Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(RadioService radio, MoodCatalog moods) : ControllerBase
{
    [HttpGet("moods")]
    public ActionResult<IReadOnlyList<string>> Moods() =>
        Ok(moods.All.Select(mood => mood.Key).ToList());

    [HttpPost("radio")]
    public async Task<ActionResult<RadioBatchDto>> Radio(RadioRequest request, CancellationToken ct) =>
        Ok(await radio.NextAsync(request, ct));
}
