using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// История прослушиваний — та, которую человек видит как «недавно слушал».
///
/// <para>
/// Это не тот же журнал, из которого растут рекомендации. Здесь запись обновляется, пока трек
/// слушают, и список подрезается до последней тысячи на пользователя: для «недавнего» так и
/// правильно, но повторы и скипы при этом теряются. Движку они нужны, поэтому он читает
/// собственный журнал событий (<c>api/events</c>), а годовая статистика — третью, почасовую
/// сводку. Подробности — в docs/backend/04-domain-model.md.
/// </para>
/// </summary>
[ApiController]
[Route("api/history")]
public class HistoryController(HistoryService history) : ControllerBase
{
    /// <summary>История с отметками времени: одна запись на прослушивание.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<HistoryEntryDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await history.GetHistoryAsync(new PageRequest(page, pageSize), ct));

    /// <summary>То же самое, свёрнутое до треков без повторов, — для полки «недавно слушали».</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Recent(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await history.GetRecentlyPlayedAsync(new PageRequest(page, pageSize), ct));

    /// <summary>
    /// Отмечает прослушивание.
    ///
    /// <para>
    /// Засчитывается не всякое включение, а только то, где прослушано не меньше
    /// <c>Playback:HistoryThresholdSeconds</c>. Повторный вызов по тому же треку в пределах
    /// получаса обновляет существующую запись, а не заводит вторую, — иначе пауза посреди песни
    /// превращалась бы в два прослушивания.
    /// </para>
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Record(RecordPlayRequest request, CancellationToken ct)
    {
        await history.RecordPlayAsync(request, ct);
        return NoContent();
    }

    /// <summary>
    /// Стирает историю текущего пользователя.
    ///
    /// <para>
    /// Только её: журнал событий и почасовая сводка остаются, поэтому рекомендации и статистика
    /// после очистки не обнуляются. Ручка убирает список с глаз, а не отменяет прослушанное.
    /// </para>
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await history.ClearAsync(ct);
        return NoContent();
    }
}
