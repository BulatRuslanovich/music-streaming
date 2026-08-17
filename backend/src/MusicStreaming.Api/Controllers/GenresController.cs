using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Жанры библиотеки.
///
/// <para>
/// Жанр здесь — не справочник, а то, что нашлось в тегах загруженных файлов: он заводится при
/// загрузке первого трека с таким названием и исчезает вместе с последним. Поэтому ручек создания
/// и правки нет.
/// </para>
/// </summary>
[ApiController]
[Route("api/genres")]
public class GenresController(CatalogService catalog) : ControllerBase
{
    /// <summary>Все жанры. Без пагинации: их столько, сколько встретилось в тегах, — десятки, а не тысячи.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> List(CancellationToken ct) =>
        Ok(await catalog.GetGenresAsync(ct));

    /// <summary>Треки жанра.</summary>
    [HttpGet("{id:guid}/tracks")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TrackDto>>> Tracks(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await catalog.GetGenreTracksAsync(id, new PageRequest(page, pageSize), ct));
}
