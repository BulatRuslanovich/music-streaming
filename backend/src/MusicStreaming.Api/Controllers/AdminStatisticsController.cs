// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Admin;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Аналитика сервиса целиком. Отдаёт только агрегаты: поштучной истории прослушиваний и полей
/// аккаунта вроде хеша пароля здесь нет и быть не должно.
/// </summary>
[ApiController]
[Route("api/admin/statistics")]
[Authorize(Policy = "Admin")]
public class AdminStatisticsController(
    AdminOverviewService overview,
    AdminListenerStatisticsService listeners,
    AdminUploadStatisticsService uploads,
    AdminCatalogHealthService catalog) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewDto>> Overview(
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Month,
        CancellationToken ct = default) =>
        Ok(await overview.GetAsync(period, ct));

    [HttpGet("catalog")]
    public async Task<ActionResult<AdminCatalogHealthDto>> Catalog(CancellationToken ct) =>
        Ok(await catalog.GetAsync(ct));

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminListenerDto>>> Users(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Month,
        [FromQuery] string? q = null,
        [FromQuery] AdminListenerSort sort = AdminListenerSort.ListenedSeconds,
        [FromQuery] SortDirection direction = SortDirection.Desc,
        CancellationToken ct = default) =>
        Ok(await listeners.GetAsync(
            new AdminListenerFilter(period, q, sort, direction), new PageRequest(page, pageSize), ct));

    [HttpGet("users/{userId:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminListenerDetailDto>> User(
        Guid userId,
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Month,
        CancellationToken ct = default) =>
        Ok(await listeners.GetDetailAsync(userId, period, ct));

    [HttpGet("uploads")]
    public async Task<ActionResult<PagedResult<AdminUploadDto>>> Uploads(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.All,
        [FromQuery] Guid? userId = null,
        [FromQuery] IngestionSource? source = null,
        [FromQuery] string? q = null,
        [FromQuery] AdminUploadSort sort = AdminUploadSort.CreatedAt,
        [FromQuery] SortDirection direction = SortDirection.Desc,
        CancellationToken ct = default) =>
        Ok(await uploads.GetAsync(
            new AdminUploadFilter(period, userId, source, q, sort, direction),
            new PageRequest(page, pageSize),
            ct));
}
