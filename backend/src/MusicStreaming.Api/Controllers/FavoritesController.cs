using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Избранное текущего пользователя.
///
/// <para>
/// Только чтение списка: добавляют и убирают из избранного через подресурс самого трека
/// (<c>POST</c> и <c>DELETE</c> на <c>api/tracks/{id}/favorite</c>), потому что отметка
/// принадлежит паре «пользователь и трек», а не отдельному объекту со своим идентификатором.
/// </para>
/// </summary>
[ApiController]
[Route("api/favorites")]
public class FavoritesController(FavoriteService favorites) : ControllerBase
{
    /// <summary>Избранные треки, свежие сверху.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await favorites.GetFavoritesAsync(new PageRequest(page, pageSize), ct));
}
