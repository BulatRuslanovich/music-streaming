// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Domain.Entities.Recommendations;

namespace App.Recommendations;

public record RecordEventsResultDto(int Accepted, int Rejected);

public class EventIngestService(
    IApplicationDbContext db,
    RecommendationRefreshQueue refreshQueue,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<EventIngestService> logger)
{
    private const int MaxBacklogDays = 7;

    private const int MaxSeconds = 86_400;

    // Без токена запроса: последний батч клиент шлёт с keepalive при закрытии страницы,
    // и разрыв соединения не должен обрывать запись.
    public async Task<RecordEventsResultDto> AcceptAsync(RecordEventsRequest request)
    {
        var reported = request.Events;
        if (reported is null || reported.Count == 0)
            return new RecordEventsResultDto(0, 0);

        var now = clock.GetUtcNow();
        var userId = currentUser.Id;
        var limit = Math.Min(reported.Count, RecommendationTuning.Maintenance.MaxEventsPerRequest);

        // Неизвестный тип или событие без своей сущности отбрасываются; время прижимается к [now - 7д, now],
        // счётчики секунд — к [0, сутки].
        var floor = now.AddDays(-MaxBacklogDays);
        var created = new List<PlaybackEvent>(limit);

        foreach (var item in reported.Take(limit))
        {
            var type = Enum.TryParse<PlaybackEventType>(item.Type, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : PlaybackEventType.Unknown;

            var requiresTrack = type
                is PlaybackEventType.TrackStarted
                or PlaybackEventType.TrackPlayed
                or PlaybackEventType.TrackCompleted
                or PlaybackEventType.TrackSkipped
                or PlaybackEventType.TrackReplayed
                or PlaybackEventType.TrackLiked
                or PlaybackEventType.TrackUnliked
                or PlaybackEventType.TrackAddedToPlaylist
                or PlaybackEventType.TrackRemovedFromPlaylist
                or PlaybackEventType.TrackAddedToQueue;

            if (type == PlaybackEventType.Unknown
                || (requiresTrack && item.TrackId is null)
                || (type is PlaybackEventType.ArtistOpened or PlaybackEventType.AlbumOpened && item.EntityId is null))
                continue;

            var occurredAt = item.OccurredAt ?? now;

            created.Add(new PlaybackEvent
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                TrackId = requiresTrack ? item.TrackId : null,
                EntityId = item.EntityId,
                Type = type,
                OccurredAt = occurredAt > now ? now : occurredAt < floor ? floor : occurredAt,
                PositionSeconds = ClampSeconds(item.PositionSeconds),
                ListenedSeconds = ClampSeconds(item.ListenedSeconds),
                DurationSeconds = ClampSeconds(item.DurationSeconds),
                SessionId = item.SessionId ?? Guid.Empty,
            });
        }

        // Трек мог быть удалён, пока событие ждало отправки в очереди клиента.
        List<Guid> referenced = [.. created.Where(e => e.TrackId is not null).Select(e => e.TrackId!.Value).Distinct()];
        HashSet<Guid> existing = referenced.Count == 0
            ? []
            : [.. await db.Tracks.AsNoTracking().Where(t => referenced.Contains(t.Id)).Select(t => t.Id).ToListAsync()];

        var accepted = created.Where(e => e.TrackId is null || existing.Contains(e.TrackId.Value)).ToList();
        var rejected = reported.Count - accepted.Count;

        if (accepted.Count > 0)
        {
            db.PlaybackEvents.AddRange(accepted);
            await db.SaveChangesAsync();

            // Полки пересобираются сразу после событий, которые явно меняют вкус; рядовой прогресс ждёт TTL.
            var forceRefresh = accepted.Any(e => e.Type switch
            {
                PlaybackEventType.TrackCompleted
                    or PlaybackEventType.TrackReplayed
                    or PlaybackEventType.TrackLiked
                    or PlaybackEventType.TrackUnliked
                    or PlaybackEventType.TrackAddedToPlaylist
                    or PlaybackEventType.TrackRemovedFromPlaylist
                    or PlaybackEventType.TrackAddedToQueue => true,
                PlaybackEventType.TrackSkipped => EventWeights.CompletionRatio(e.ListenedSeconds, e.DurationSeconds)
                    is < 0.20 or >= 0.80,
                _ => false,
            });

            refreshQueue.MarkDirty(userId, now, forceRefresh);
        }

        if (rejected > 0)
            logger.LogDebug("Discarded {Rejected} of {Total} reported events", rejected, reported.Count);

        return new RecordEventsResultDto(accepted.Count, rejected);
    }

    private static int ClampSeconds(int? value) => value is null or < 0 ? 0 : Math.Min(value.Value, MaxSeconds);
}
