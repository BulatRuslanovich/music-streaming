// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/albums")]
public class AlbumsController(CatalogService catalog, StreamingService streaming) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AlbumDto>>> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] Guid? artistId,
        [FromQuery] string? q = null,
        [FromQuery] bool filterByRecent = false,
        CancellationToken ct = default) =>
        Ok(await catalog.GetAlbumsAsync(new PageRequest(page, pageSize), artistId, filterByRecent, q, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlbumDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetAlbumAsync(id, ct));

    [HttpGet("{id:guid}/cover")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cover(
        Guid id, [FromQuery] CoverSize size = CoverSize.Full, CancellationToken ct = default) =>
        this.ImageFile(await streaming.OpenAlbumCoverAsync(id, size, ct));
}
