// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Common;

public static class ProjectionLookups
{
    public static async Task<Dictionary<Guid, TrackDto>> TracksByIdAsync(
        this IApplicationDbContext db, Guid userId, IEnumerable<Guid> trackIds, CancellationToken ct = default)
    {
        var ids = Distinct(trackIds);
        if (ids.Count == 0)
            return [];

        return await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(Projections.Track(userId))
            .ToDictionaryAsync(t => t.Id, ct);
    }

    public static async Task<Dictionary<Guid, ArtistDto>> ArtistsByIdAsync(
        this IApplicationDbContext db, IEnumerable<Guid> artistIds, CancellationToken ct = default)
    {
        var ids = Distinct(artistIds);
        if (ids.Count == 0)
            return [];

        return await db.Artists.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(Projections.Artist)
            .ToDictionaryAsync(a => a.Id, ct);
    }

    public static async Task<Dictionary<Guid, AlbumDto>> AlbumsByIdAsync(
        this IApplicationDbContext db, IEnumerable<Guid> albumIds, CancellationToken ct = default)
    {
        var ids = Distinct(albumIds);
        if (ids.Count == 0)
            return [];

        return await db.Albums.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(Projections.Album)
            .ToDictionaryAsync(a => a.Id, ct);
    }

    private static List<Guid> Distinct(IEnumerable<Guid> ids) => [.. ids.Distinct()];
}
