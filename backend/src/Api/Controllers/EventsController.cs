// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Recommendations;

namespace Api.Controllers;

[ApiController]
[Route("api/playback/signals")]
public class EventsController(EventIngestService ingest) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RecordEventsResultDto>> Record(RecordEventsRequest request) =>
        Accepted(await ingest.AcceptAsync(request));
}
