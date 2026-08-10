using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/playlists")]
public sealed class PlaylistsController(PlaylistService playlists) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> List(CancellationToken ct) =>
        Ok(await playlists.GetPlaylistsAsync(ct));

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

    /// <summary>Applies a new track order, as produced by dragging rows in the playlist view.</summary>
    [HttpPut("{id:guid}/tracks/order")]
    public async Task<IActionResult> Reorder(Guid id, ReorderPlaylistRequest request, CancellationToken ct)
    {
        await playlists.ReorderAsync(id, request.TrackIds ?? [], ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/favorites")]
public sealed class FavoritesController(FavoriteService favorites) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TrackDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await favorites.GetFavoritesAsync(new PageRequest(page, pageSize), ct));
}

[ApiController]
[Route("api/history")]
public sealed class HistoryController(HistoryService history) : ControllerBase
{
    /// <summary>The raw play log, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<HistoryEntryDto>>> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await history.GetHistoryAsync(new PageRequest(page, pageSize), ct));

    /// <summary>Distinct tracks ordered by most recent play — what "Recently Played" shows.</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<PagedResult<TrackDto>>> Recent(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await history.GetRecentlyPlayedAsync(new PageRequest(page, pageSize), ct));

    [HttpPost]
    public async Task<IActionResult> Record(RecordPlayRequest request, CancellationToken ct)
    {
        await history.RecordPlayAsync(request, ct);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await history.ClearAsync(ct);
        return NoContent();
    }
}
