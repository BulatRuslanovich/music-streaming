using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class PlaylistService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMusicStorage storage,
    IImageProcessor imageProcessor,
    IOptions<StorageOptions> storageOptions,
    TimeProvider clock,
    ILogger<PlaylistService> logger)
{
    private const int MaxNameLength = 200;

    public async Task<IReadOnlyList<PlaylistDto>> GetPlaylistsAsync(CancellationToken ct = default) =>
        await db.Playlists.AsNoTracking()
            .Where(p => p.UserId == currentUser.Id)
            .OrderBy(p => p.Name)
            .Select(Projections.Playlist)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PlaylistDto>> GetPublicPlaylistsAsync(CancellationToken ct = default) =>
        await db.Playlists.AsNoTracking()
            .Where(p => p.IsPublic)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(Projections.Playlist)
            .ToListAsync(ct);

    public async Task<PlaylistDetailDto> GetPlaylistAsync(Guid id, CancellationToken ct = default)
    {
        var playlist = await db.Playlists.AsNoTracking()
            .Where(p => p.Id == id && (p.UserId == currentUser.Id || p.IsPublic))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.IsPublic,
                p.UserId,
                OwnerName = p.User!.DisplayName,
                p.CoverPath,
                p.CreatedAt,
                p.UpdatedAt,
            })
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
            playlist.IsPublic,
            playlist.UserId,
            playlist.OwnerName,
            tracks.Sum(t => t.DurationSeconds),
            playlist.CoverPath is not null,
            tracks.FirstOrDefault(t => t.HasCover)?.Id,
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
            Description = Text.TrimToNull(request.Description),
            IsPublic = request.IsPublic,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Playlist {PlaylistId} created by user {UserId} (public: {IsPublic})",
            playlist.Id, currentUser.Id, playlist.IsPublic);

        return await ProjectAsync(playlist.Id, ct);
    }

    public async Task<PlaylistDto> UpdateAsync(Guid id, UpdatePlaylistRequest request, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(id, ct);

        playlist.Name = ValidateName(request.Name);
        playlist.Description = Text.TrimToNull(request.Description);
        playlist.IsPublic = request.IsPublic;
        playlist.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);

        return await ProjectAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(id, ct);
        var coverPath = playlist.CoverPath;

        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(ct);

        if (coverPath is not null)
            storage.Delete(coverPath);

        logger.LogInformation("Playlist {PlaylistId} deleted", id);
    }

    public async Task<PlaylistDto> SetCoverAsync(
        Guid id,
        Stream content,
        string? contentType,
        string fileName,
        long length,
        CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(id, ct);

        ImageUpload.Validate(contentType, fileName, length, storageOptions.Value.MaxImageUploadBytes);

        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var webp = await imageProcessor.ToSquareWebpAsync(buffered, ImageUpload.Edge, ct);

        playlist.CoverPath = await storage.SavePlaylistCoverAsync(playlist.Id, webp, ct);
        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cover set for playlist {PlaylistId} ({Bytes} bytes)", id, webp.Length);
        return await ProjectAsync(id, ct);
    }

    public async Task RemoveCoverAsync(Guid id, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(id, ct);

        var path = playlist.CoverPath;
        if (path is null)
            return;

        playlist.CoverPath = null;
        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        storage.Delete(path);
        logger.LogInformation("Cover removed from playlist {PlaylistId}", id);
    }

    public async Task AddTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);

        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");

        var now = clock.GetUtcNow();

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO playlist_tracks (id, playlist_id, track_id, position, added_at)
            SELECT {Guid.CreateVersion7()}, {playlistId}, {trackId},
                   COALESCE(MAX(position), -1) + 1, {now}
            FROM playlist_tracks
            WHERE playlist_id = {playlistId}
            ON CONFLICT (playlist_id, track_id) DO NOTHING
            """, ct);

        playlist.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);

        var removed = await db.PlaylistTracks
            .Where(pt => pt.PlaylistId == playlistId && pt.TrackId == trackId)
            .ExecuteDeleteAsync(ct);

        if (removed == 0)
            throw new NotFoundException("The track is not in this playlist.");

        await RenumberAsync(playlistId, ct);

        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid playlistId, IReadOnlyList<Guid> trackIds, CancellationToken ct = default)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);
        var wanted = trackIds.Distinct().ToArray();

        if (wanted.Length > 0)
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE playlist_tracks pt
                SET position = ordered.position
                FROM (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               ORDER BY COALESCE(wanted.ordinality, 2147483647), pt.position, pt.id) - 1
                               AS position
                    FROM playlist_tracks pt
                    LEFT JOIN unnest({wanted}) WITH ORDINALITY AS wanted(track_id, ordinality)
                           ON wanted.track_id = pt.track_id
                    WHERE pt.playlist_id = {playlistId}
                ) ordered
                WHERE pt.id = ordered.id AND pt.position <> ordered.position
                """, ct);
        }

        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private Task RenumberAsync(Guid playlistId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            UPDATE playlist_tracks pt
            SET position = ranked.position
            FROM (
                SELECT id, ROW_NUMBER() OVER (ORDER BY position, added_at, id) - 1 AS position
                FROM playlist_tracks
                WHERE playlist_id = {playlistId}
            ) ranked
            WHERE pt.id = ranked.id AND pt.position <> ranked.position
            """, ct);

    private Task<PlaylistDto> ProjectAsync(Guid id, CancellationToken ct) =>
        db.Playlists.AsNoTracking().Where(p => p.Id == id).Select(Projections.Playlist).FirstAsync(ct);

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
}
