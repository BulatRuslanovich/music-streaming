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
    /// <param name="sectionSize">Сколько элементов показывать в каждой секции.</param>
    /// <param name="debug">Запросить сырые оценки — действует только для администратора, см. <see cref="IncludeScores"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    [HttpGet("home")]
    public async Task<ActionResult<RecommendationHomeDto>> Home(
        [FromQuery] int sectionSize = 12,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetHomeAsync(sectionSize, IncludeScores(debug), ct));

    /// <summary>Персональная лента треков.</summary>
    /// <param name="page">Номер страницы.</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <param name="debug">Запросить сырые оценки — только для администратора.</param>
    /// <param name="ct">Токен отмены.</param>
    [HttpGet("tracks")]
    public async Task<ActionResult<PagedResult<RecommendedTrackDto>>> Tracks(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool debug = false,
        CancellationToken ct = default) =>
        Ok(await recommendations.GetTracksAsync(new PageRequest(page, pageSize), IncludeScores(debug), ct));

    /// <summary>Рекомендованные исполнители.</summary>
    /// <param name="limit">Максимум исполнителей в ответе.</param>
    /// <param name="ct">Токен отмены.</param>
    [HttpGet("artists")]
    public async Task<ActionResult<IReadOnlyList<ArtistDto>>> Artists(
        [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetArtistsAsync(limit, ct));

    /// <summary>Рекомендованные альбомы.</summary>
    /// <param name="limit">Максимум альбомов в ответе.</param>
    /// <param name="ct">Токен отмены.</param>
    [HttpGet("albums")]
    public async Task<ActionResult<IReadOnlyList<AlbumDto>>> Albums(
        [FromQuery] int limit = 12, CancellationToken ct = default) =>
        Ok(await recommendations.GetAlbumsAsync(limit, ct));

    /// <summary>Треки, похожие на указанный.</summary>
    /// <param name="trackId">Трек, для которого ищутся похожие.</param>
    /// <param name="limit">Максимум треков в ответе.</param>
    /// <param name="debug">Запросить сырые оценки похожести — только для администратора.</param>
    /// <param name="ct">Токен отмены.</param>
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
    /// <param name="debug">Флаг из query-параметра запроса.</param>
    /// <returns>Истина, только если <paramref name="debug"/> установлен и текущий пользователь — администратор.</returns>
    private bool IncludeScores(bool debug) => debug && User.IsInRole(AppRoles.Admin);
}
