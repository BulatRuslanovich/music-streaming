using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Исполнители.
///
/// <para>
/// Заводятся не здесь, а при загрузке треков, из тегов: имя нормализуется и ищется среди
/// существующих, поэтому «The Beatles» и «the  beatles» — один исполнитель. Ручки создания нет
/// вовсе, а править можно только то, чего нет в файлах: отображаемое имя и фотографию.
/// </para>
/// </summary>
[ApiController]
[Route("api/artists")]
public class ArtistsController(CatalogService catalog, ArtistProfileService profiles, StreamingService streaming) : ControllerBase
{
    /// <summary>Список исполнителей.</summary>
    /// <param name="q">Подстрока имени; без неё возвращаются все.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ArtistDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q,
        CancellationToken ct = default) =>
        Ok(await catalog.GetArtistsAsync(new PageRequest(page, pageSize), q, ct));

    /// <summary>Исполнитель вместе со страницей его треков.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArtistDetailDto>> Get(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default) =>
        Ok(await catalog.GetArtistAsync(id, new PageRequest(page, pageSize), ct));

    /// <summary>
    /// Фотография исполнителя, webp.
    ///
    /// <para>
    /// 404, если фотографии нет: у большинства исполнителей она не появляется сама — в тегах
    /// аудиофайлов её не бывает, и подтягивает её отдельная утилита (см. профиль <c>tools</c> в
    /// docker compose).
    /// </para>
    /// </summary>
    [HttpGet("{id:guid}/image")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct) =>
        this.ImageFile(await streaming.OpenArtistImageAsync(id, ct));

    /// <summary>
    /// Переименовывает исполнителя.
    ///
    /// <para>
    /// 409, если под новым именем уже есть другой: слияние двух исполнителей в одного — не
    /// переименование, и делать его молча нельзя.
    /// </para>
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArtistDto>> Update(
        Guid id, UpdateArtistRequest request, CancellationToken ct) =>
        Ok(await profiles.RenameAsync(id, request, ct));

    /// <summary>Загружает фотографию исполнителя; она приводится к квадрату и перекодируется в webp.</summary>
    [HttpPost("{id:guid}/image")]
    [Authorize(Policy = AppPolicies.Admin)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ArtistDto>> UploadImage(Guid id, IFormFile? file, CancellationToken ct)
    {
        var image = file.RequireImage();

        await using var stream = image.OpenReadStream();
        return Ok(await profiles.SetImageAsync(
            id, stream, image.ContentType, image.FileName, image.Length, ct));
    }

    /// <summary>Убирает фотографию исполнителя.</summary>
    [HttpDelete("{id:guid}/image")]
    [Authorize(Policy = AppPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct)
    {
        await profiles.RemoveImageAsync(id, ct);
        return NoContent();
    }
}
