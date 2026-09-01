// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Api.Startup;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
// Путь намеренно не содержит "events": блокировщики рекламы режут такие URL
// как аналитику (ERR_BLOCKED_BY_CLIENT), и телеметрия проигрывания не доходит.
[Route("api/playback/signals")]
public class EventsController(EventIngestService ingest) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RequestPipelineSetup.EventsPolicy)]
    public ActionResult<RecordEventsResultDto> Record(RecordEventsRequest request) =>
        Accepted(ingest.Accept(request));
}
