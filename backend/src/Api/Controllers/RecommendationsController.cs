// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services.Recommendations;

namespace Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(RadioService radio) : ControllerBase
{
    [HttpPost("radio")]
    public async Task<ActionResult<RadioBatchDto>> Radio(RadioRequest request, CancellationToken ct) =>
        Ok(await radio.NextAsync(request, ct));
}
