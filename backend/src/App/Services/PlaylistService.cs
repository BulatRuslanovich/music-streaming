// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Imaging;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace App.Services;

public class PlaylistService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    FileSystemMusicStorage storage,
    FileSystemImageStorage images,
    ImageSharpImageProcessor imageProcessor,
    TimeProvider clock)
{

    public async Task<IReadOnlyList<PlaylistDto>> GetPlaylistsAsync(CancellationToken ct) =>
        await db.Playlists.AsNoTracking()
            .Where(p => p.UserId == currentUser.Id)
            .OrderBy(p => p.Name)
            .Select(ToDto.Playlist)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PlaylistDto>> GetPublicPlaylistsAsync(CancellationToken ct) =>
        await db.Playlists.AsNoTracking()
            .Where(p => p.IsPublic)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(ToDto.Playlist)
            .ToListAsync(ct);

    public async Task<PlaylistDetailDto> GetPlaylistAsync(Guid id, CancellationToken ct)
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
                OwnerName = p.User!.Username,
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
            .Select(ToDto.Track(currentUser.Id))
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

    public async Task<PlaylistDto> CreateAsync(CreatePlaylistRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var playlist = new Playlist
        {
            UserId = currentUser.Id,
            Name = request.Name.Trim(),
            Description = Text.TrimToNull(request.Description),
            IsPublic = request.IsPublic,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);


        return await ProjectAsync(playlist.Id, ct);
    }

    public async Task<PlaylistDto> UpdateAsync(Guid id, UpdatePlaylistRequest request, CancellationToken ct)
    {
        var playlist = await LoadOwnedAsync(id, ct);

        playlist.Name = request.Name.Trim();
        playlist.Description = Text.TrimToNull(request.Description);
        playlist.IsPublic = request.IsPublic;
        playlist.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);

        return await ProjectAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var playlist = await LoadOwnedAsync(id, ct);
        var coverPath = playlist.CoverPath;

        db.Playlists.Remove(playlist);
        await db.SaveChangesAsync(ct);

        if (coverPath is not null)
            storage.Delete(coverPath);

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

        var renditions = await ImageUpload.AcceptSquareWebpSetAsync(
            imageProcessor, content, contentType, fileName, length,
            UploadLimits.ImageBytes, ct);

        playlist.CoverPath = await images.SavePlaylistCoverAsync(playlist.Id, renditions, ct);
        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(id, ct);
    }

    public async Task RemoveCoverAsync(Guid id, CancellationToken ct)
    {
        var playlist = await LoadOwnedAsync(id, ct);

        var path = playlist.CoverPath;
        if (path is null)
            return;

        playlist.CoverPath = null;
        playlist.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        images.DeleteCover(path);
    }

    public async Task AddTracksAsync(Guid playlistId, IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        var playlist = await LoadOwnedAsync(playlistId, ct);
        var wanted = trackIds.Distinct().ToArray();

        if (wanted.Length == 0)
            throw new ValidationException("At least one track id is required.");

        var known = await db.Tracks.CountAsync(t => wanted.Contains(t.Id), ct);
        if (known != wanted.Length)
            throw new NotFoundException("Track not found.");

        var ids = wanted.Select(_ => Guid.CreateVersion7()).ToArray();
        var now = clock.GetUtcNow();

        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO playlist_tracks (id, playlist_id, track_id, position)
            SELECT added.id, {playlistId}, added.track_id,
                   (SELECT COALESCE(MAX(position), -1) FROM playlist_tracks WHERE playlist_id = {playlistId})
                       + added.ordinality
            FROM unnest({wanted}, {ids}) WITH ORDINALITY AS added(track_id, id, ordinality)
            ON CONFLICT (playlist_id, track_id) DO NOTHING
            """, ct);

        if (inserted < wanted.Length)
            await RenumberAsync(playlistId, ct);

        playlist.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct)
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

    public async Task ReorderAsync(Guid playlistId, IReadOnlyList<Guid> trackIds, CancellationToken ct)
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
                SELECT id, ROW_NUMBER() OVER (ORDER BY position, id) - 1 AS position
                FROM playlist_tracks
                WHERE playlist_id = {playlistId}
            ) ranked
            WHERE pt.id = ranked.id AND pt.position <> ranked.position
            """, ct);

    private Task<PlaylistDto> ProjectAsync(Guid id, CancellationToken ct) =>
        db.Playlists.AsNoTracking().Where(p => p.Id == id).Select(ToDto.Playlist).FirstAsync(ct);

    private async Task<Playlist> LoadOwnedAsync(Guid id, CancellationToken ct) =>
        await db.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.UserId == currentUser.Id, ct)
        ?? throw new NotFoundException("Playlist not found.");
}
