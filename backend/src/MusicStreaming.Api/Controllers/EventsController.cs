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
    ///
    /// Всегда успешен: неразобранные или незнакомые события считаются и отбрасываются, а не валят
    /// запрос, потому что клиент всё равно не сделает с отказом ничего полезного и уж точно не
    /// должен из-за него переставать играть музыку.
    /// </summary>
    /// <param name="request">Пачка сырых событий от клиента.</param>
    /// <returns>202 Accepted со сводкой, сколько событий принято и сколько отклонено.</returns>
    [HttpPost]
    [EnableRateLimiting("events")]
    public ActionResult<RecordEventsResultDto> Record(RecordEventsRequest request) =>
        Accepted(ingest.Accept(request));
}
