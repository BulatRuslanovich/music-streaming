// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/library")]
public class LibraryController(CatalogService catalog, LibraryImportService import) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<LibraryOverviewDto>> Overview(
        [FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await catalog.GetLibraryOverviewAsync(Math.Clamp(sectionSize, 1, 50), ct));

    [HttpGet("import")]
    [Authorize(Policy = "Admin")]
    public ActionResult<LibraryImportStatusDto> ImportStatus(CancellationToken ct) =>
        Ok(import.Status(ct));

    [HttpPost("import")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LibraryImportStatusDto>> Import(CancellationToken ct) =>
        Ok(await import.ImportAsync(ct));
}
