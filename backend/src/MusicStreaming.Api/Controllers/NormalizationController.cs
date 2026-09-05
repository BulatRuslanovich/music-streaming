// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/tracks/{id:guid}/normalization")]
public class NormalizationController(NormalizationService normalization) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NormalizationDto>> Get(Guid id, [FromQuery] string mode = "track", CancellationToken ct = default) =>
        Ok(await normalization.GetAsync(id, mode, ct));
}
