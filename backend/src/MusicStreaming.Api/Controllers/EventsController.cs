using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Collects the behavioural signals the recommendation engine learns from.
/// </summary>
[ApiController]
[Route("api/events")]
public class EventsController(EventIngestService ingest) : ControllerBase
{
    /// <summary>
    /// Accepts a batch of events.
    ///
    /// Always succeeds: unparsable or unknown events are counted and dropped rather than failing
    /// the request, because a client cannot do anything useful with a rejection and should never
    /// stop playing music over one.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("events")]
    public ActionResult<RecordEventsResultDto> Record(RecordEventsRequest request) =>
        Accepted(ingest.Accept(request));
}
