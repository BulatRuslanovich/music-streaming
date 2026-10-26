// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using App.Dtos;
using App.Services.Integrations;

namespace Api.Controllers;

// Экспериментальный импорт из Яндекс Музыки, только для админа.
[ApiController]
[Route("api/integrations/yandex-music")]
[Authorize(Policy = "Admin")]
public class YandexMusicController(YandexMusicService yandex) : ControllerBase
{
    public const string TokenHeader = "X-Yandex-Music-Token";

    [HttpGet("search")]
    public async Task<ActionResult<YandexSearchDto>> Search(
        [FromHeader(Name = TokenHeader)] string? token, [FromQuery] string? q, CancellationToken ct) =>
        Ok(await yandex.SearchAsync(token, q, ct));

    [HttpGet("imports")]
    public ActionResult<IReadOnlyList<YandexImportDto>> Imports() => Ok(yandex.List());

    [HttpPost("imports")]
    public ActionResult<YandexImportDto> Start(
        [FromHeader(Name = TokenHeader)] string? token, StartYandexImportRequest request) =>
        Accepted(yandex.Start(token, request));

    [HttpDelete("imports/{id:guid}")]
    public IActionResult Remove(Guid id)
    {
        yandex.Remove(id);
        return NoContent();
    }
}
