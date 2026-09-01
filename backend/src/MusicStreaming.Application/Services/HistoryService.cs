// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class HistoryService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IOptions<PlaybackOptions> options,
    TimeProvider clock,
    ILogger<HistoryService> logger)
{
    private const int TrimSlack = 100;

    public int HistoryThresholdSeconds => options.Value.HistoryThresholdSeconds;

    public async Task<PagedResult<HistoryEntryDto>> GetHistoryAsync(PageRequest page, CancellationToken ct)
    {
        var query = db.ListeningHistory.AsNoTracking().Where(h => h.UserId == currentUser.Id);
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(h => h.PlayedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(h => new { h.Id, h.TrackId, h.PlayedAt, h.PlaybackPosition })
            .ToListAsync(ct);

        var tracks = await db.TracksByIdAsync(currentUser.Id, rows.Select(r => r.TrackId), ct);

        var items = rows
            .Where(r => tracks.ContainsKey(r.TrackId))
            .Select(r => new HistoryEntryDto(r.Id, tracks[r.TrackId], r.PlayedAt, r.PlaybackPosition))
            .ToList();

        return new PagedResult<HistoryEntryDto>(items, total, page.Page, page.PageSize);
    }

    public async Task<PagedResult<TrackDto>> GetRecentlyPlayedAsync(PageRequest page, CancellationToken ct)
    {
        var grouped = db.ListeningHistory.AsNoTracking()
            .Where(h => h.UserId == currentUser.Id)
            .GroupBy(h => h.TrackId)
            .Select(g => new { TrackId = g.Key, PlayedAt = g.Max(h => h.PlayedAt) });

        var total = await grouped.CountAsync(ct);

        var pageRows = await grouped
            .OrderByDescending(x => x.PlayedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        var tracks = await db.TracksByIdAsync(currentUser.Id, pageRows.Select(x => x.TrackId), ct);

        var ordered = pageRows
            .Where(x => tracks.ContainsKey(x.TrackId))
            .Select(x => tracks[x.TrackId])
            .ToList();

        return new PagedResult<TrackDto>(ordered, total, page.Page, page.PageSize);
    }

    public async Task RecordPlayAsync(RecordPlayRequest request, CancellationToken ct)
    {
        if (request.PlaybackPosition < HistoryThresholdSeconds)
        {
            throw new ValidationException(
                $"A play is only recorded after {HistoryThresholdSeconds} seconds of listening.");
        }

        await db.RequireTrackAsync(request.TrackId, ct);

        var now = clock.GetUtcNow();
        var dedupeWindow = now.AddMinutes(-30);

        var recent = await db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id
                        && h.TrackId == request.TrackId
                        && h.PlayedAt >= dedupeWindow)
            .OrderByDescending(h => h.PlayedAt)
            .FirstOrDefaultAsync(ct);

        if (recent is not null)
        {
            recent.PlayedAt = now;
            recent.PlaybackPosition = request.PlaybackPosition;
        }
        else
        {
            db.ListeningHistory.Add(new ListeningHistoryEntry
            {
                UserId = currentUser.Id,
                TrackId = request.TrackId,
                PlayedAt = now,
                PlaybackPosition = request.PlaybackPosition,
            });
        }

        await db.SaveChangesAsync(ct);
        await TrimAsync(ct);

        logger.LogDebug("Recorded play of track {TrackId} at {Position}s", request.TrackId, request.PlaybackPosition);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id)
            .ExecuteDeleteAsync(ct);
    }

    private async Task TrimAsync(CancellationToken ct)
    {
        var retain = options.Value.HistoryRetentionEntries;

        var overflowing = await OldestBeyondAsync(retain + TrimSlack, ct) is not null;
        if (!overflowing)
            return;

        if (await OldestBeyondAsync(retain, ct) is not { } cutoff)
            return;

        await db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id && h.PlayedAt <= cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private Task<DateTimeOffset?> OldestBeyondAsync(int keep, CancellationToken ct) =>
        db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id)
            .OrderByDescending(h => h.PlayedAt)
            .Skip(keep)
            .Select(h => (DateTimeOffset?)h.PlayedAt)
            .FirstOrDefaultAsync(ct);
}
