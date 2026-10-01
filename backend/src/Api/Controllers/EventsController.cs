// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services.Recommendations;

namespace Api.Controllers;

[ApiController]
[Route("api/playback/signals")]
public class EventsController(EventIngestService ingest) : ControllerBase
{
    [HttpPost]
    public ActionResult<RecordEventsResultDto> Record(RecordEventsRequest request) =>
        Accepted(ingest.Accept(request));
}
