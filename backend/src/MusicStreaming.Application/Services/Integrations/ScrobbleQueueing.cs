using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Integrations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Integrations;

public record ScrobblePayload(
    string Artist,
    string Title,
    string? Album,
    int DurationSeconds,
    long? PlayedAtUnix);

public static class ScrobbleRules
{
    public const int MinimumTrackSeconds = 30;
    public const int LongEnoughSeconds = 240;

    public static bool Qualifies(int listenedSeconds, int durationSeconds) =>
        durationSeconds > MinimumTrackSeconds
        && listenedSeconds >= Math.Min(durationSeconds / 2, LongEnoughSeconds);
}

public class ScrobbleQueueing(
    IApplicationDbContext db,
    OutboundJobQueue queue,
    ILogger<ScrobbleQueueing> logger)
{
    private static readonly JsonSerializerOptions PayloadFormat = new(JsonSerializerDefaults.Web);

    public async Task QueueAsync(IReadOnlyList<PlaybackEvent> batch, CancellationToken ct = default)
    {
        var relevant = batch
            .Where(e => e.TrackId is not null && e.Type is
                PlaybackEventType.TrackStarted
                or PlaybackEventType.TrackCompleted
                or PlaybackEventType.TrackSkipped)
            .ToList();

        if (relevant.Count == 0)
            return;

        var userIds = relevant.Select(e => e.UserId).Distinct().ToList();

        var connected = await db.LastfmAccounts.AsNoTracking()
            .Where(a => a.Enabled && userIds.Contains(a.UserId))
            .Select(a => a.UserId)
            .ToListAsync(ct);

        if (connected.Count == 0)
            return;

        var listeners = connected.ToHashSet();
        var mine = relevant.Where(e => listeners.Contains(e.UserId)).ToList();

        var trackIds = mine.Select(e => e.TrackId!.Value).Distinct().ToList();

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Title,
                Artist = t.Artist!.Name,
                Album = t.Album == null ? null : t.Album.Title,
                t.DurationSeconds,
            })
            .ToDictionaryAsync(t => t.Id, ct);

        var jobs = new List<OutboundJob>();

        foreach (var playbackEvent in mine)
        {
            if (!tracks.TryGetValue(playbackEvent.TrackId!.Value, out var track))
                continue;

            if (playbackEvent.Type == PlaybackEventType.TrackStarted)
            {
                jobs.Add(Job(
                    OutboundJobKind.LastfmNowPlaying,
                    playbackEvent.UserId,
                    $"lastfm:now:{playbackEvent.UserId}:{track.Id}:{Minute(playbackEvent.OccurredAt)}",
                    new ScrobblePayload(track.Artist, track.Title, track.Album, track.DurationSeconds, null)));

                continue;
            }

            if (PlayAttempt.From(playbackEvent) is not { } attempt)
                continue;

            if (!ScrobbleRules.Qualifies(attempt.ListenedSeconds, track.DurationSeconds))
                continue;

            jobs.Add(Job(
                OutboundJobKind.LastfmScrobble,
                playbackEvent.UserId,
                $"lastfm:scrobble:{playbackEvent.UserId}:{track.Id}:{Minute(attempt.StartedAt)}",
                new ScrobblePayload(
                    track.Artist,
                    track.Title,
                    track.Album,
                    track.DurationSeconds,
                    attempt.StartedAt.ToUnixTimeSeconds())));
        }

        var queued = await queue.EnqueueAsync(jobs, ct);
        if (queued > 0)
            logger.LogDebug("Queued {Count} Last.fm jobs", queued);
    }

    private static OutboundJob Job(
        OutboundJobKind kind, Guid userId, string dedupeKey, ScrobblePayload payload) => new()
        {
            Kind = kind,
            UserId = userId,
            DedupeKey = dedupeKey,
            Payload = JsonSerializer.Serialize(payload, PayloadFormat),
        };

    public static ScrobblePayload? ReadPayload(string payload) =>
        JsonSerializer.Deserialize<ScrobblePayload>(payload, PayloadFormat);

    private static long Minute(DateTimeOffset at) => at.ToUnixTimeSeconds() / 60;
}
