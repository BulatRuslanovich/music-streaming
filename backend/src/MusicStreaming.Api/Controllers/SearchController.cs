// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>Поиск по библиотеке сразу по исполнителям, альбомам, трекам и жанрам.</summary>
[ApiController]
[Route("api/search")]
public class SearchController(SearchService search) : ControllerBase
{
    /// <summary>
    /// Ищет по всем видам объектов одним запросом.
    /// </summary>
    /// <param name="q">Строка поиска. Пустая или слишком короткая возвращает пустой результат, а не ошибку.</param>
    /// <param name="limit">Сколько объектов вернуть в каждой группе.</param>
    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await search.SearchAsync(q, limit, ct));
}
