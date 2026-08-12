using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Превращает сообщённый сигнал в пригодное для записи событие — или отвергает его.
///
/// <para>
/// Телеметрии от клиента здесь не доверяют никогда. Часы врут, сборки устаревают, а страница,
/// проспавшая в фоновой вкладке, просыпается с бессмысленными позициями. Всё, что здесь есть,
/// либо зажимает значение в диапазон, который не может испортить модель, либо отбрасывает событие
/// целиком — молча, потому что клиент всё равно ничего полезного с жалобой не сделает.
/// </para>
/// </summary>
public static class PlaybackEventFactory
{
    /// <summary>События старше этого срока подтягиваются вперёд: залежавшийся батч не должен переписывать историю.</summary>
    public const int MaxBacklogDays = 7;

    /// <summary>Ничего не играет дольше суток; всё, что выше, — сломанный счётчик.</summary>
    public const int MaxSeconds = 86_400;

    private const int MaxPlatformLength = 32;

    /// <summary>
    /// Собирает доменное событие или возвращает null, когда сообщению нельзя доверять.
    /// </summary>
    public static PlaybackEvent? TryCreate(
        PlaybackEventRequest request,
        Guid userId,
        DateTimeOffset now)
    {
        var type = ParseType(request.Type);
        if (type == PlaybackEventType.Unknown)
            return null;

        if (RequiresTrack(type) && request.TrackId is null)
            return null;

        if (RequiresEntity(type) && request.EntityId is null)
            return null;

        var occurredAt = Clamp(request.OccurredAt ?? now, now);
        var duration = ClampSeconds(request.DurationSeconds);
        var listened = ClampSeconds(request.ListenedSeconds);

        return new PlaybackEvent
        {
            // Назначается здесь, а не базой, чтобы идентификатор — UUIDv7 — упорядочивал события по
            // моменту приёма сервером.
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TrackId = RequiresTrack(type) ? request.TrackId : null,
            EntityId = request.EntityId,
            Type = type,
            OccurredAt = occurredAt,
            PositionSeconds = ClampSeconds(request.PositionSeconds),
            ListenedSeconds = listened,
            DurationSeconds = duration,
            SessionId = request.SessionId ?? Guid.Empty,
            Source = ParseSource(request.Source),
            SourceId = request.SourceId,
            Platform = NormalizePlatform(request.Platform),
        };
    }

    public static PlaybackEventType ParseType(string? value) =>
        Enum.TryParse<PlaybackEventType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : PlaybackEventType.Unknown;

    public static PlaybackSource ParseSource(string? value) =>
        Enum.TryParse<PlaybackSource>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : PlaybackSource.Unknown;

    /// <summary>События, которые описывают трек и бессмысленны без него.</summary>
    public static bool RequiresTrack(PlaybackEventType type) => type
        is PlaybackEventType.TrackStarted
        or PlaybackEventType.TrackPlayed
        or PlaybackEventType.TrackCompleted
        or PlaybackEventType.TrackSkipped
        or PlaybackEventType.TrackPaused
        or PlaybackEventType.TrackReplayed
        or PlaybackEventType.TrackLiked
        or PlaybackEventType.TrackUnliked
        or PlaybackEventType.TrackAddedToPlaylist
        or PlaybackEventType.TrackRemovedFromPlaylist
        or PlaybackEventType.TrackAddedToQueue;

    /// <summary>События, которые описывают исполнителя, альбом или плейлист, а не трек.</summary>
    public static bool RequiresEntity(PlaybackEventType type) => type
        is PlaybackEventType.ArtistOpened
        or PlaybackEventType.AlbumOpened
        or PlaybackEventType.PlaylistOpened;

    /// <summary>
    /// Возвращает клиентскую отметку времени в правдоподобное окно. Спешащее устройство иначе
    /// поставило бы событие в будущее, где затухание держало бы его вечно свежим, а отстающее —
    /// похоронило бы реальное прослушивание под кривой затухания.
    /// </summary>
    private static DateTimeOffset Clamp(DateTimeOffset reported, DateTimeOffset now)
    {
        if (reported > now)
            return now;

        var floor = now.AddDays(-MaxBacklogDays);
        return reported < floor ? floor : reported;
    }

    private static int ClampSeconds(int? value) => value is null or < 0 ? 0 : Math.Min(value.Value, MaxSeconds);

    private static string NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return "web";

        var trimmed = platform.Trim();
        return trimmed.Length <= MaxPlatformLength ? trimmed : trimmed[..MaxPlatformLength];
    }
}
