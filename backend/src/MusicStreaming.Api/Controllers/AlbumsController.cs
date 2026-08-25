// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/albums")]
public class AlbumsController(
    CatalogService catalog,
    AlbumEditService editor,
    StreamingService streaming) : ControllerBase
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

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlbumDto>> Update(Guid id, UpdateAlbumRequest request, CancellationToken ct) =>
        Ok(await editor.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/cover")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<AlbumDto>> UploadCover(Guid id, IFormFile? file, CancellationToken ct)
    {
        var image = file.RequireImage();

        await using var stream = image.OpenReadStream();
        return Ok(await editor.SetCoverAsync(id, stream, image.ContentType, image.FileName, image.Length, ct));
    }

    [HttpDelete("{id:guid}/cover")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCover(Guid id, CancellationToken ct)
    {
        await editor.RemoveCoverAsync(id, ct);
        return NoContent();
    }
}
