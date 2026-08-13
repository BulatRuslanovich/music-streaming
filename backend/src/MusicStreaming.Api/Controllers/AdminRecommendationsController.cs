using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Диагностика движка рекомендаций. Только на чтение и только для администраторов.
/// </summary>
[ApiController]
[Route("api/admin/recommendations")]
[Authorize(Policy = AppPolicies.Admin)]
public class AdminRecommendationsController(RecommendationDiagnosticsService diagnostics) : ControllerBase
{
    /// <summary>Моментальный снимок здоровья движка рекомендаций — объём данных, состояние кэша полок и вовлечённость пользователей.</summary>
    /// <param name="ct">Токен отмены.</param>
    [HttpGet("stats")]
    public async Task<ActionResult<RecommendationStatsDto>> Stats(CancellationToken ct) =>
        Ok(await diagnostics.GetStatsAsync(ct));
}
