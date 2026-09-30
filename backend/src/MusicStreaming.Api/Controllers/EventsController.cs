// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/playback/signals")]
public class EventsController(EventIngestService ingest) : ControllerBase
{
    [HttpPost]
    public ActionResult<RecordEventsResultDto> Record(RecordEventsRequest request) =>
        Accepted(ingest.Accept(request));
}
