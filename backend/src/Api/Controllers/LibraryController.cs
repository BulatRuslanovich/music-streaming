// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/library")]
public class LibraryController(LibraryOverviewService overview, LibraryMapService map) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<LibraryOverviewDto>> Overview(
        [FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await overview.GetLibraryOverviewAsync(Math.Clamp(sectionSize, 1, 50), ct));

    [HttpGet("map")]
    public async Task<ActionResult<LibraryMapDto>> Map(CancellationToken ct) =>
        Ok(await map.GetMapAsync(ct));
}
