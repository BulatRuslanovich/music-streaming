// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

public class ConnectTrackService(IApplicationDbContext db, ICurrentUser user)
{
    public async Task<IReadOnlyList<TrackDto>> GetAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length > 5000) throw new ValidationException("Queue is too large.");
        var tracks = await db.TracksByIdAsync(user.Id, ids, ct);
        if (ids.Any(id => !tracks.ContainsKey(id))) throw new NotFoundException("A track in this queue was removed.");
        return [.. ids.Select(id => tracks[id])];
    }
}
