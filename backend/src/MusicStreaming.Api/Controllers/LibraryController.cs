// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/library")]
public class LibraryController(CatalogService catalog) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<LibraryOverviewDto>> Overview(
        [FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await catalog.GetLibraryOverviewAsync(Math.Clamp(sectionSize, 1, 50), ct));
}
