using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Integrations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Integrations;

/// <summary>Что именно отправляется в Last.fm; форма полезной нагрузки задания.</summary>
/// <param name="PlayedAtUnix">Момент начала проигрывания; <c>null</c> — это «сейчас играет».</param>
public record ScrobblePayload(
    string Artist,
    string Title,
    string? Album,
    int DurationSeconds,
    long? PlayedAtUnix);

/// <summary>Когда прослушивание засчитывается.</summary>
public static class ScrobbleRules
{
    /// <summary>Треки короче этого Last.fm не принимает вовсе.</summary>
    public const int MinimumTrackSeconds = 30;

    /// <summary>Долгий трек засчитывается, не дожидаясь половины.</summary>
    public const int LongEnoughSeconds = 240;

    /// <summary>
    /// Правило самого Last.fm: половина трека или четыре минуты — что наступит раньше. Не порог
    /// истории Caimack (тридцать секунд): профиль в Last.fm складывается из всех клиентов сразу, и
    /// считать по-своему значило бы засчитывать вдвое больше, чем любой другой проигрыватель.
    /// </summary>
    public static bool Qualifies(int listenedSeconds, int durationSeconds) =>
        durationSeconds > MinimumTrackSeconds
        && listenedSeconds >= Math.Min(durationSeconds / 2, LongEnoughSeconds);
}

/// <summary>
/// Превращает поведенческие события в исходящие задания Last.fm.
///
/// <para>
/// Источник — тот же поток прослушивания, из которого растут история и статистика, а не события
/// плеера в браузере: клиент не должен ни знать про Last.fm, ни ждать его, ни повторять за него
/// запросы. Здесь же считается и время начала проигрывания, которое Last.fm ждёт в отметке
/// прослушивания.
/// </para>
/// </summary>
public class ScrobbleQueueing(
    IApplicationDbContext db,
    OutboundJobQueue queue,
    ILogger<ScrobbleQueueing> logger)
{
    private static readonly JsonSerializerOptions PayloadFormat = new(JsonSerializerDefaults.Web);

    /// <summary>Ставит в очередь всё, что заслужил этот пакет событий.</summary>
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

            // Длительность берётся у трека, а не из события: клиент мог знать её до правки тегов.
            if (!ScrobbleRules.Qualifies(attempt.ListenedSeconds, track.DurationSeconds))
                continue;

            jobs.Add(Job(
                OutboundJobKind.LastfmScrobble,
                playbackEvent.UserId,
                // Ключ по началу проигрывания: одно и то же проигрывание, дошедшее до нас дважды,
                // даёт один и тот же ключ, а честный повторный запуск того же трека — другой.
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
