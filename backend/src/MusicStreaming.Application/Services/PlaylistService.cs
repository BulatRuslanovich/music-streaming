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

    /// <summary>
    /// Витрина публичных плейлистов: их видно всем, включая собственные, чтобы владелец видел свой
    /// плейлист ровно таким, каким его видят остальные. Свежеобновлённые идут первыми.
    /// </summary>
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

        // Проекцией, а не вручную: имя владельца живёт в другой таблице.
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

    /// <summary>Убирает картинку; плитка откатывается к обложке альбома первого трека.</summary>
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
