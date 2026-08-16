using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/playlists")]
public class PlaylistsController(PlaylistService playlists, StreamingService streaming) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> List(CancellationToken ct) =>
        Ok(await playlists.GetPlaylistsAsync(ct));

    /// <summary>Публичные плейлисты всех пользователей. Маршрут не конфликтует с <c>{id:guid}</c>: «public» не guid.</summary>
    [HttpGet("public")]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> Public(CancellationToken ct) =>
        Ok(await playlists.GetPublicPlaylistsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlaylistDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await playlists.GetPlaylistAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<PlaylistDto>> Create(CreatePlaylistRequest request, CancellationToken ct)
    {
        var playlist = await playlists.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = playlist.Id }, playlist);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlaylistDto>> Update(Guid id, UpdatePlaylistRequest request, CancellationToken ct) =>
        Ok(await playlists.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await playlists.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/cover")]
    public async Task<IActionResult> Cover(Guid id, CancellationToken ct) =>
        this.ImageFile(await streaming.OpenPlaylistCoverAsync(id, ct));

    [HttpPost("{id:guid}/cover")]
    public async Task<ActionResult<PlaylistDto>> UploadCover(Guid id, IFormFile? file, CancellationToken ct)
    {
        var image = file.RequireImage();

        await using var stream = image.OpenReadStream();
        return Ok(await playlists.SetCoverAsync(
            id, stream, image.ContentType, image.FileName, image.Length, ct));
    }

    [HttpDelete("{id:guid}/cover")]
    public async Task<IActionResult> DeleteCover(Guid id, CancellationToken ct)
    {
        await playlists.RemoveCoverAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/tracks")]
    public async Task<IActionResult> AddTrack(Guid id, AddPlaylistTrackRequest request, CancellationToken ct)
    {
        await playlists.AddTrackAsync(id, request.TrackId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/tracks/{trackId:guid}")]
    public async Task<IActionResult> RemoveTrack(Guid id, Guid trackId, CancellationToken ct)
    {
        await playlists.RemoveTrackAsync(id, trackId, ct);
        return NoContent();
    }

    /// <summary>Применяет новый порядок треков, полученный перетаскиванием строк плейлиста.</summary>
    [HttpPut("{id:guid}/tracks/order")]
    public async Task<IActionResult> Reorder(Guid id, ReorderPlaylistRequest request, CancellationToken ct)
    {
        await playlists.ReorderAsync(id, request.TrackIds ?? [], ct);
        return NoContent();
    }
}

