using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public sealed class HistoryService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IOptions<PlaybackOptions> options,
    TimeProvider clock,
    ILogger<HistoryService> logger)
{
    public int HistoryThresholdSeconds => options.Value.HistoryThresholdSeconds;

    /// <summary>
    /// The raw history, newest first — one row per play, so the same track can appear repeatedly.
    /// </summary>
    public async Task<PagedResult<HistoryEntryDto>> GetHistoryAsync(PageRequest page, CancellationToken ct = default)
    {
        var query = db.ListeningHistory.AsNoTracking().Where(h => h.UserId == currentUser.Id);
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(h => h.PlayedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(h => new { h.Id, h.TrackId, h.PlayedAt, h.PlaybackPosition })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.TrackId).Distinct().ToList();
        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(Projections.Track(currentUser.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var items = rows
            .Where(r => tracks.ContainsKey(r.TrackId))
            .Select(r => new HistoryEntryDto(r.Id, tracks[r.TrackId], r.PlayedAt, r.PlaybackPosition))
            .ToList();

        return new PagedResult<HistoryEntryDto>(items, total, page.Page, page.PageSize);
    }

    /// <summary>
    /// The "Recently Played" view: distinct tracks ordered by their most recent play.
    /// </summary>
    public async Task<PagedResult<TrackDto>> GetRecentlyPlayedAsync(PageRequest page, CancellationToken ct = default)
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

        var ids = pageRows.Select(x => x.TrackId).ToList();
        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(Projections.Track(currentUser.Id))
            .ToListAsync(ct);

        // Re-apply the recency order that the id round trip lost.
        var ordered = pageRows
            .Join(tracks, x => x.TrackId, t => t.Id, (_, t) => t)
            .ToList();

        return new PagedResult<TrackDto>(ordered, total, page.Page, page.PageSize);
    }

    /// <summary>
    /// Records a play once the configured listening threshold has been reached. Repeated calls
    /// for the same track within the threshold window update the existing row instead of adding
    /// a new one, so a page refresh mid-track does not inflate the history.
    /// </summary>
    public async Task RecordPlayAsync(RecordPlayRequest request, CancellationToken ct = default)
    {
        if (request.PlaybackPosition < HistoryThresholdSeconds)
        {
            throw new ValidationException(
                $"A play is only recorded after {HistoryThresholdSeconds} seconds of listening.");
        }

        if (!await db.Tracks.AnyAsync(t => t.Id == request.TrackId, ct))
            throw new NotFoundException("Track not found.");

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

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Keeps the history bounded so it cannot grow without limit over years of use.</summary>
    private async Task TrimAsync(CancellationToken ct)
    {
        var retain = options.Value.HistoryRetentionEntries;
        var count = await db.ListeningHistory.CountAsync(h => h.UserId == currentUser.Id, ct);
        if (count <= retain)
            return;

        var cutoff = await db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id)
            .OrderByDescending(h => h.PlayedAt)
            .Skip(retain)
            .Select(h => h.PlayedAt)
            .FirstOrDefaultAsync(ct);

        if (cutoff == default)
            return;

        await db.ListeningHistory
            .Where(h => h.UserId == currentUser.Id && h.PlayedAt <= cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
