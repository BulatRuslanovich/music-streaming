// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Плейлисты.
/// </summary>
[ApiController]
[Route("api/playlists")]
public class PlaylistsController(PlaylistService playlists, StreamingService streaming) : ControllerBase
{
    /// <summary>Плейлисты текущего пользователя.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> List(CancellationToken ct) =>
        Ok(await playlists.GetPlaylistsAsync(ct));

    /// <summary>Публичные плейлисты всех пользователей. Маршрут не конфликтует с <c>{id:guid}</c>: «public» не guid.</summary>
    [HttpGet("public")]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> Public(CancellationToken ct) =>
        Ok(await playlists.GetPublicPlaylistsAsync(ct));

    /// <summary>Плейлист вместе с треками в заданном пользователем порядке.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaylistDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await playlists.GetPlaylistAsync(id, ct));

    /// <summary>Создаёт пустой плейлист.</summary>
    [HttpPost]
    [ProducesResponseType<PlaylistDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlaylistDto>> Create(CreatePlaylistRequest request, CancellationToken ct)
    {
        var playlist = await playlists.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = playlist.Id }, playlist);
    }

    /// <summary>Меняет название, описание и признак публичности.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaylistDto>> Update(Guid id, UpdatePlaylistRequest request, CancellationToken ct) =>
        Ok(await playlists.UpdateAsync(id, request, ct));

    /// <summary>Удаляет плейлист. Сами треки остаются в библиотеке — уходят только строки об их принадлежности.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await playlists.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Собственная обложка плейлиста, webp. Видна владельцу, а у публичного плейлиста — всем.</summary>
    [HttpGet("{id:guid}/cover")]
    [Produces("image/webp", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cover(Guid id, CancellationToken ct) =>
        this.ImageFile(await streaming.OpenPlaylistCoverAsync(id, ct));

    /// <summary>Загружает обложку плейлиста; она приводится к квадрату и перекодируется в webp.</summary>
    [HttpPost("{id:guid}/cover")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<PlaylistDto>> UploadCover(Guid id, IFormFile? file, CancellationToken ct)
    {
        var image = file.RequireImage();

        await using var stream = image.OpenReadStream();
        return Ok(await playlists.SetCoverAsync(
            id, stream, image.ContentType, image.FileName, image.Length, ct));
    }

    /// <summary>Убирает обложку плейлиста.</summary>
    [HttpDelete("{id:guid}/cover")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCover(Guid id, CancellationToken ct)
    {
        await playlists.RemoveCoverAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Добавляет трек в конец плейлиста.
    /// </summary>
    [HttpPost("{id:guid}/tracks")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddTrack(Guid id, AddPlaylistTrackRequest request, CancellationToken ct)
    {
        await playlists.AddTrackAsync(id, request.TrackId, ct);
        return NoContent();
    }

    /// <summary>Убирает трек из плейлиста и перенумеровывает оставшиеся.</summary>
    [HttpDelete("{id:guid}/tracks/{trackId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveTrack(Guid id, Guid trackId, CancellationToken ct)
    {
        await playlists.RemoveTrackAsync(id, trackId, ct);
        return NoContent();
    }

    /// <summary>Применяет новый порядок треков, полученный перетаскиванием строк плейлиста.</summary>
    [HttpPut("{id:guid}/tracks/order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(Guid id, ReorderPlaylistRequest request, CancellationToken ct)
    {
        await playlists.ReorderAsync(id, request.TrackIds ?? [], ct);
        return NoContent();
    }
}

