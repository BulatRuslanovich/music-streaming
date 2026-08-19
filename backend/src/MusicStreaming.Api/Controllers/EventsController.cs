// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Собирает поведенческие сигналы, на которых учится движок рекомендаций.
/// </summary>
[ApiController]
[Route("api/events")]
public class EventsController(EventIngestService ingest) : ControllerBase
{
    /// <summary>
    /// Принимает пачку событий.
    /// </summary>
    /// <param name="request">Пачка сырых событий от клиента.</param>
    /// <returns>202 Accepted со сводкой, сколько событий принято и сколько отклонено.</returns>
    [HttpPost]
    [EnableRateLimiting("events")]
    public ActionResult<RecordEventsResultDto> Record(RecordEventsRequest request) =>
        Accepted(ingest.Accept(request));
}
