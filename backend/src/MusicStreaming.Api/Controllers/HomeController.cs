using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;


[ApiController]
[Route("api/home")]
public class HomeController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HomeSummaryDto>> Get([FromQuery] int sectionSize = 12, CancellationToken ct = default) =>
        Ok(await catalog.GetHomeSummaryAsync(Math.Clamp(sectionSize, 1, 50), ct));
}
