// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/library/duplicates")]
[Authorize(Policy = "Admin")]
public class DuplicatesController(DuplicateService duplicates) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DuplicateReportDto>> Report(CancellationToken ct) =>
        Ok(await duplicates.GetReportAsync(ct));

    [HttpPost("dismiss")]
    public async Task<IActionResult> Dismiss(DismissDuplicatesRequest request, CancellationToken ct)
    {
        await duplicates.DismissAsync(request.TrackIds ?? [], ct);
        return NoContent();
    }

    [HttpPost("merge")]
    public async Task<IActionResult> Merge(MergeDuplicatesRequest request, CancellationToken ct)
    {
        await duplicates.MergeAsync(request.KeepId, request.RemoveIds ?? [], ct);
        return NoContent();
    }
}
