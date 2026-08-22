// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController(SearchService search) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await search.SearchAsync(q, limit, ct));
}
