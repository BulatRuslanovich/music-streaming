using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>Всё, что относится к самому вошедшему пользователю: его настройки, его пароль, его статистика.</summary>
[ApiController]
[Route("api/me")]
public class MeController(
    UserSettingsService settings,
    StatisticsService statistics,
    AuthService auth,
    ICurrentUser currentUser,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings(CancellationToken ct) =>
        Ok(UserSettingsService.Describe(await settings.GetAsync(ct)));

    /// <summary>Частичное обновление: приходят только изменившиеся поля.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(
        UpdateUserSettingsRequest request, CancellationToken ct) =>
        Ok(await settings.UpdateAsync(request, ct));

    [HttpGet("statistics")]
    public async Task<ActionResult<StatisticsDto>> Statistics(
        [FromQuery] StatisticsPeriod period = StatisticsPeriod.Month, CancellationToken ct = default) =>
        Ok(await statistics.GetAsync(period, ct));

    /// <summary>
    /// Смена собственного пароля. Прежние сессии отзываются, а текущая тут же получает новые куки —
    /// иначе смена пароля выкидывала бы из приложения того, кто её и затеял.
    /// </summary>
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(request, currentUser.Id, ct);
        AuthCookies.Write(Response, result, AuthCookies.RequireSecure(Request, environment));

        return NoContent();
    }
}
