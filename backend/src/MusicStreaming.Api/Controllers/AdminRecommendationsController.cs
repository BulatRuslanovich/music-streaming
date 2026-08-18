using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/admin/recommendations")]
[Authorize(Policy = "Admin")]
public class AdminRecommendationsController(RecommendationDiagnosticsService diagnostics) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<RecommendationStatsDto>> Stats(CancellationToken ct) =>
        Ok(await diagnostics.GetStatsAsync(ct));
}
