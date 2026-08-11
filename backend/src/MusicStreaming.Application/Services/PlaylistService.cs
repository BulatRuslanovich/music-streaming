using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class PlaylistService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<PlaylistService> logger)
{
    private const int MaxNameLength = 200;

    public async Task<IReadOnlyList<PlaylistDto>> GetPlaylistsAsync(CancellationToken ct = default) =>
        await db.Playlists.AsNoTracking()
            .Where(p => p.UserId == currentUser.Id)
            .OrderBy(p => p.Name)
            .Select(p => new PlaylistDto(
                p.Id, p.Name, p.Description, p.Tracks.Count,
                p.Tracks.Sum(pt => pt.Track!.DurationSeconds), p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

    public async Task<PlaylistDetailDto> GetPlaylistAsync(Guid id, CancellationToken ct = default)
    {
        var playlist = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == id && p.UserId == currentUser.Id)
            .Select(p => new { p.Id, p.Name, p.Description, p.CreatedAt, p.UpdatedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Playlist not found.");

        var tracks = await db.PlaylistTracks.AsNoTracking()
            .Where(pt => pt.PlaylistId == id)
            .OrderBy(pt => pt.Position)
            .Select(pt => pt.Track!)
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        return new PlaylistDetailDto(
            playlist.Id,
            playlist.Name,
            playlist.Description,
            tracks.Sum(t => t.DurationSeconds),
            playlist.CreatedAt,
            playlist.UpdatedAt,
            tracks);
    }

    public async Task<PlaylistDto> CreateAsync(CreatePlaylistRequest request, CancellationToken ct = default)
    {
        var name = ValidateName(request.Name);
        var now = clock.GetUtcNow();

        var playlist = new Playlist
        {
            UserId = currentUser.Id,
            Name = name,
            Description = Trim(request.Description),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Playlist {PlaylistId} created by user {UserId}", playlist.Id, currentUser.Id);
        return new PlaylistDto(playlist.Id, playlist.Name, playlist.Description, 0, 0, playlist.CreatedAt, playlist.UpdatedAt);
    }

    public async Task<PlaylistDto> UpdateAsync(Guid id, UpdatePlaylistRequest request, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(id, ct);

        playlist.Name = ValidateName(request.Name);
        playlist.Description = Trim(request.Description);
        playlist.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);

        var trackCount = await db.PlaylistTracks.CountAsync(pt => pt.PlaylistId == id, ct);
        var duration = await db.PlaylistTracks
            .Where(pt => pt.PlaylistId == id)
            .SumAsync(pt => pt.Track!.DurationSeconds, ct);

        return new PlaylistDto(
            playlist.Id, playlist.Name, playlist.Description,
            trackCount, duration, playlist.CreatedAt, playlist.UpdatedAt);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(id, ct);
        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Playlist {PlaylistId} deleted", id);
    }

    public async Task AddTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);

        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");

        var nextPosition = await db.PlaylistTracks
            .Where(pt => pt.PlaylistId == playlistId)
            .MaxAsync(pt => (int?)pt.Position, ct) ?? -1;

        db.PlaylistTracks.Add(new PlaylistTrack
        {
            PlaylistId = playlistId,
            TrackId = trackId,
            Position = nextPosition + 1,
            AddedAt = clock.GetUtcNow(),
        });

        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);

        var entries = await db.PlaylistTracks
            .Where(pt => pt.PlaylistId == playlistId)
            .OrderBy(pt => pt.Position)
            .ToListAsync(ct);

        var target = entries.FirstOrDefault(pt => pt.TrackId == trackId)
            ?? throw new NotFoundException("The track is not in this playlist.");

        db.PlaylistTracks.Remove(target);

        var remaining = entries.Where(pt => pt.Id != target.Id).ToList();
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].Position = i;

        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid playlistId, IReadOnlyList<Guid> trackIds, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);

        var entries = await db.PlaylistTracks
            .Where(pt => pt.PlaylistId == playlistId)
            .OrderBy(pt => pt.Position)
            .ToListAsync(ct);

        var remaining = new List<PlaylistTrack>(entries);
        var ordered = new List<PlaylistTrack>(entries.Count);

        foreach (var trackId in trackIds)
        {
            var match = remaining.FirstOrDefault(pt => pt.TrackId == trackId);
            if (match is null)
                continue;

            remaining.Remove(match);
            ordered.Add(match);
        }

        ordered.AddRange(remaining);

        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Position = i;

        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private async Task<Playlist> LoadOwnedAsync(Guid id, CancellationToken ct) =>
        await db.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.UserId == currentUser.Id, ct)
        ?? throw new NotFoundException("Playlist not found.");

    private static string ValidateName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException("Playlist name is required.");
        if (trimmed.Length > MaxNameLength)
            throw new ValidationException($"Playlist name must be at most {MaxNameLength} characters.");
        return trimmed;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
