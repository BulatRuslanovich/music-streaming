// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/recap")]
public class RecapController(RecapService recap) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecapMonthDto>>> Months(CancellationToken ct) =>
        Ok(await recap.MonthsAsync(ct));

    [HttpGet("{year:int}/{month:int}")]
    public async Task<ActionResult<RecapDto>> Month(int year, int month, CancellationToken ct) =>
        Ok(await recap.GetAsync(year, month, ct));
}
