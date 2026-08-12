using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Персональные эндпойнты на чтение. Всё здесь отдаётся из заранее посчитанных полок.
/// </summary>
[ApiController]
[Route("api/recommendations")]
public class RecommendationsController(RecommendationService recommendations) : ControllerBase
{
    /// <summary>Персональная главная страница: все полки по порядку.</summary>
    [HttpGet("home")]
    public async Task<ActionResult<RecommendationHomeDto>> Home(
        [FromQuery] int sectionSize = 12,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetHomeAsync(sectionSize, IncludeScores(debug), ct));

    /// <summary>Персональная лента треков.</summary>
    [HttpGet("tracks")]
    public async Task<ActionResult<PagedResult<RecommendedTrackDto>>> Tracks(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetTracksAsync(new PageRequest(page, pageSize), IncludeScores(debug), ct));

    [HttpGet("artists")]
    public async Task<ActionResult<IReadOnlyList<ArtistDto>>> Artists(
        [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetArtistsAsync(limit, ct));

    [HttpGet("albums")]
    public async Task<ActionResult<IReadOnlyList<AlbumDto>>> Albums(
        [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetAlbumsAsync(limit, ct));

    /// <summary>Треки, похожие на указанный.</summary>
    [HttpGet("similar/{trackId:guid}")]
    public async Task<ActionResult<IReadOnlyList<RecommendedTrackDto>>> Similar(
        Guid trackId,
        [FromQuery] int limit = 20,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetSimilarAsync(trackId, limit, IncludeScores(debug), ct));

    /// <summary>
    /// Оценки релевантности — отладочный вывод, а не то, что показывают слушателю, поэтому они
    /// заполняются только когда их запросил администратор.
    /// </summary>
    private bool IncludeScores(bool debug) => debug && User.IsInRole(AppRoles.Admin);
}
