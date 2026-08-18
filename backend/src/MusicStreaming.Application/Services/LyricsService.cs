using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class LyricsService(IApplicationDbContext db, TimeProvider clock, ILogger<LyricsService> logger)
{
    public async Task<LyricsDto?> GetAsync(Guid trackId, CancellationToken ct = default)
    {
        var lyrics = await db.TrackLyrics.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TrackId == trackId, ct);

        return lyrics is null ? null : Describe(lyrics);
    }

    public void AttachFromMetadata(Guid trackId, AudioMetadata metadata)
    {
        var parsed = metadata.SyncedLyrics.Count > 0
            ? LyricsText.FromTimedLines(metadata.SyncedLyrics)
            : LyricsText.Parse(metadata.Lyrics);

        if (parsed.IsEmpty)
            return;

        db.TrackLyrics.Add(new TrackLyrics
        {
            TrackId = trackId,
            Plain = parsed.Plain,
            Synced = parsed.Lines,
            Source = LyricsSource.Embedded,
            UpdatedAt = clock.GetUtcNow(),
        });

        logger.LogDebug(
            "Track {TrackId} carries {Kind} lyrics", trackId, parsed.Lines.Count > 0 ? "synced" : "plain");
    }

    public async Task<LyricsDto?> ReplaceAsync(
        Guid trackId, string? text, CancellationToken ct = default)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");

        var existing = await db.TrackLyrics.FirstOrDefaultAsync(l => l.TrackId == trackId, ct);
        var parsed = LyricsText.Parse(text);

        if (parsed.IsEmpty)
        {
            if (existing is not null)
                db.TrackLyrics.Remove(existing);

            await db.SaveChangesAsync(ct);
            return null;
        }

        if (existing is null)
        {
            existing = new TrackLyrics { TrackId = trackId };
            db.TrackLyrics.Add(existing);
        }

        existing.Plain = parsed.Plain;
        existing.Synced = parsed.Lines;
        existing.Source = LyricsSource.Manual;
        existing.UpdatedAt = clock.GetUtcNow();

        await db.SaveChangesAsync(ct);
        return Describe(existing);
    }

    private static LyricsDto Describe(TrackLyrics lyrics) => new(
        lyrics.TrackId,
        lyrics.Plain,
        [.. lyrics.Synced.Select(line => new LyricLineDto(line.At, line.Text))],
        lyrics.Source);
}
