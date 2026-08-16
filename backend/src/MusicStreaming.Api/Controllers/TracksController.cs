using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/tracks")]
public class TracksController(
    CatalogService catalog,
    TrackEditService editor,
    TrackUploadService upload,
    UploadProbeService uploadProbe,
    StreamingService streaming,
    FavoriteService favorites,
    LyricsService lyrics,
    RecommendationService recommendations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q = null,
        [FromQuery] CatalogService.TrackSort sort = CatalogService.TrackSort.Title,
        CancellationToken ct = default) =>
        Ok(await catalog.GetTracksAsync(new PageRequest(page, pageSize), sort, q, ct));

    /// <summary>
    /// Случайный набор треков из всей библиотеки, а не из открытой страницы. Поиск учитывается:
    /// если список сужен, перемешивается только он.
    /// </summary>
    [HttpGet("shuffle")]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> Shuffle(
        [FromQuery] int? limit = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default) =>
        Ok(await catalog.GetShuffledTracksAsync(limit, q, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrackDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetTrackAsync(id, ct));

    /// <summary>
    /// Аудиопоток трека. Без параметра <paramref name="quality"/> берётся ступень из настроек
    /// пользователя — так офлайн-загрузчик и любой клиент, не знающий про качество, всё равно
    /// получают то, что человек выбрал.
    /// </summary>
    [HttpGet("{id:guid}/stream")]
    public async Task<IActionResult> Stream(
        Guid id, [FromQuery] AudioQuality? quality = null, CancellationToken ct = default)
    {
        var audio = await streaming.OpenTrackAsync(id, quality, ct);

        Response.Headers.CacheControl = "private, max-age=604800";

        return File(
            audio.Content,
            audio.ContentType,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var audio = await streaming.OpenTrackAsync(id, AudioQuality.Original, ct);

        Response.Headers.CacheControl = "private, no-store";

        return File(
            audio.Content,
            audio.ContentType,
            audio.DownloadName,
            lastModified: null,
            entityTag: EntityTagHeaderValue.Parse(audio.ETag),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Треки, похожие на этот. Дублирует <c>/api/recommendations/similar/{trackId}</c>; обе записи
    /// существуют потому, что подресурсы трека по соглашению живут здесь, а всё персональное собрано
    /// под маршрутом рекомендаций.
    /// </summary>
    [HttpGet("{id:guid}/similar")]
    public async Task<ActionResult<IReadOnlyList<RecommendedTrackDto>>> Similar(
        Guid id, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await recommendations.GetSimilarAsync(id, limit, includeScores: false, ct));

    /// <summary>
    /// Текст трека. 204, когда текста нет: его отсутствие — обычное дело, а не ошибка, и клиент
    /// не должен показывать из-за этого ни одной тревожной надписи.
    /// </summary>
    [HttpGet("{id:guid}/lyrics")]
    public async Task<ActionResult<LyricsDto>> Lyrics(Guid id, CancellationToken ct) =>
        await lyrics.GetAsync(id, ct) is { } found ? Ok(found) : NoContent();

    /// <summary>Ручная правка текста: принимается и обычный текст, и LRC. Пустое тело удаляет текст.</summary>
    [HttpPut("{id:guid}/lyrics")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<ActionResult<LyricsDto>> UpdateLyrics(
        Guid id, UpdateLyricsRequest request, CancellationToken ct) =>
        await lyrics.ReplaceAsync(id, request.Text, ct) is { } saved ? Ok(saved) : NoContent();

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenTrackCoverAsync(id, size, ct));

    /// <summary>
    /// Спрашивает про файлы до их отправки: клиент присылает хеш и теги, сервер отвечает, что из
    /// этого уже лежит в библиотеке. Совпавшее не приходится загружать вообще.
    /// </summary>
    [HttpPost("upload/check")]
    public async Task<ActionResult<UploadProbeResultDto>> CheckUpload(
        UploadProbeRequest request, CancellationToken ct) =>
        Ok(await uploadProbe.ProbeAsync(request.Files ?? [], ct));

    [HttpPost("upload")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<ActionResult<UploadResultDto>> Upload(
        [FromForm(Name = "files")] IFormFileCollection? files,
        CancellationToken ct)
    {
        var incoming = files is { Count: > 0 } ? files : Request.Form.Files;
        if (incoming.Count == 0)
            throw new ValidationException("No files were provided.");

        var candidates = incoming
            .Select(f => new UploadCandidate(f.FileName, f.ContentType, f.Length, f.OpenReadStream))
            .ToList();

        var result = await upload.UploadAsync(candidates, ct);

        if (result.Uploaded.Count == 0)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<ActionResult<TrackDto>> Update(Guid id, UpdateTrackRequest request, CancellationToken ct) =>
        Ok(await editor.UpdateTrackAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await editor.DeleteTrackAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Удаление набором. Отдельным маршрутом, а не телом у DELETE: тело у DELETE не определено
    /// стандартом, и промежуточные узлы вправе его выбросить.
    ///
    /// <para>
    /// Отвечает 200 даже когда всё названное уже было удалено — в отличие от загрузки, которая при
    /// полном отказе отвечает 400. Удаление идемпотентно по природе, загрузка нет.
    /// </para>
    /// </summary>
    [HttpPost("bulk-delete")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<ActionResult<BulkDeleteResultDto>> BulkDelete(
        BulkDeleteTracksRequest request, CancellationToken ct) =>
        Ok(await editor.DeleteTracksAsync(request.Ids ?? [], ct));

    [HttpPost("{id:guid}/favorite")]
    public async Task<IActionResult> AddFavorite(Guid id, CancellationToken ct)
    {
        await favorites.AddAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/favorite")]
    public async Task<IActionResult> RemoveFavorite(Guid id, CancellationToken ct)
    {
        await favorites.RemoveAsync(id, ct);
        return NoContent();
    }
}
