using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Одно завершившееся проигрывание, восстановленное из закрывающего его события.
///
/// <para>
/// Существует потому, что «сколько слушали» нельзя сложить по всем событиям: heartbeat присылает
/// накопленный итог этого же проигрывания, поэтому суммирование посчитало бы одно и то же по
/// нескольку раз. Итог верен ровно в закрывающем событии — дослушал или бросил, — и только оттуда
/// его берут и статистика, и отправка в Last.fm, чтобы два счёта не разошлись.
/// </para>
/// </summary>
/// <param name="TrackId">Что играло.</param>
/// <param name="StartedAt">Когда проигрывание началось — событие приходит в конце, поэтому позиция вычитается из его времени.</param>
/// <param name="ListenedSeconds">Реально прослушанные секунды.</param>
/// <param name="DurationSeconds">Длительность трека, какой её знал клиент.</param>
public readonly record struct PlayAttempt(
    Guid TrackId,
    DateTimeOffset StartedAt,
    int ListenedSeconds,
    int DurationSeconds)
{
    /// <summary>Проигрывание, если это событие его закрывает; иначе <c>null</c>.</summary>
    public static PlayAttempt? From(PlaybackEvent playbackEvent)
    {
        if (playbackEvent.TrackId is not { } trackId)
            return null;

        if (playbackEvent.Type is not (PlaybackEventType.TrackCompleted or PlaybackEventType.TrackSkipped))
            return null;

        var position = Math.Clamp(playbackEvent.PositionSeconds, 0, MaxTrackSeconds);

        return new PlayAttempt(
            trackId,
            playbackEvent.OccurredAt.AddSeconds(-position),
            Math.Clamp(playbackEvent.ListenedSeconds, 0, MaxTrackSeconds),
            Math.Clamp(playbackEvent.DurationSeconds, 0, MaxTrackSeconds));
    }

    /// <summary>Час, к которому относится проигрывание, — ключ почасовой сводки.</summary>
    public DateTimeOffset Hour => new(
        StartedAt.UtcDateTime.Date.AddHours(StartedAt.UtcDateTime.Hour), TimeSpan.Zero);

    /// <summary>Сутки не бывают длиннее суток: защита от сбитых часов клиента и испорченных длительностей.</summary>
    private const int MaxTrackSeconds = 24 * 60 * 60;
}
