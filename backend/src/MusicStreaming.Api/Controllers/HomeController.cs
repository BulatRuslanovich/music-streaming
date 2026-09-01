// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/home")]
public class HomeController(LibraryOverviewService overview, HomeFeedService feed) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HomeSummaryDto>> Get([FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await overview.GetHomeSummaryAsync(Math.Clamp(sectionSize, 1, 50), ct));

    [HttpGet("feed")]
    public async Task<ActionResult<HomeFeedDto>> Feed([FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await feed.GetAsync(Math.Clamp(sectionSize, 1, 50), ct));

    [HttpGet("mixes/{kind}")]
    public async Task<ActionResult<HomeMixDto>> Mix(HomeMixKind kind, CancellationToken ct = default) =>
        Ok(await feed.GetMixAsync(kind, ct));
}
