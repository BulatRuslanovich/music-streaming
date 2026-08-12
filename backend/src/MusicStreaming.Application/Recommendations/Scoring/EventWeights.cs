using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Превращает одно поведенческое событие в знаковый вес вкуса.
///
/// <para>
/// Кривая дослушивания — сердце всего движка: сколько трека человек на самом деле послушал,
/// говорит о его вкусе куда больше, чем сам факт нажатия play. Трек, брошенный через пять секунд,
/// — довод против; трек, дослушанный до конца, — довод за; а осознанные действия вроде лайка или
/// добавления в плейлист весят больше обоих, потому что чего-то стоят пользователю.
/// </para>
///
/// <para>
/// Намеренно чистый класс без зависимостей: это та часть, которая обязана быть доказуемо верной,
/// поэтому она не принимает ни часов, ни базы, ни объекта настроек.
/// </para>
/// </summary>
public static class EventWeights
{
    // Пороги дослушивания. Полосы широкие, потому что прослушивание шумное: телефонный звонок,
    // оборвавший трек, не должен читаться как отказ — поэтому 20–50 % лишь слегка отрицательны.
    public const double AbandonedWeight = -1.0;   // < 5 %  — включил и отбросил
    public const double DroppedWeight = -0.5;     // < 20 % — дал шанс и сказал «нет»
    public const double PartialWeight = -0.1;     // < 50 % — неоднозначно, скорее против
    public const double SustainedWeight = 0.3;    // < 80 % — досидел
    public const double NearCompleteWeight = 0.8; // ≥ 80 % — практически дослушал

    public const double CompletedWeight = 1.0;
    public const double ReplayedWeight = 0.8;
    public const double LikedWeight = 2.5;
    public const double UnlikedWeight = -2.5;
    public const double PlaylistAddWeight = 2.0;
    public const double PlaylistRemoveWeight = -1.5;
    public const double QueueAddWeight = 0.8;

    /// <summary>Просмотр страницы исполнителя, альбома, плейлиста или клик по результату поиска.</summary>
    public const double EntityInterestWeight = 0.2;

    /// <summary>
    /// Вес прослушивания по доле услышанного.
    /// </summary>
    public static double ForCompletion(double ratio) => ratio switch
    {
        < 0.05 => AbandonedWeight,
        < 0.20 => DroppedWeight,
        < 0.50 => PartialWeight,
        < 0.80 => SustainedWeight,
        _ => NearCompleteWeight,
    };

    /// <summary>
    /// Вклад события в аффинити пользователя к его треку.
    /// Возвращает ноль для событий, не несущих оценки: они всё равно обновляют прослушанные
    /// секунды и свежесть, но их учёт продублировал бы то прослушивание, к которому они относятся.
    /// </summary>
    public static double ForTrack(PlaybackEventType type, double completionRatio) => type switch
    {
        PlaybackEventType.TrackSkipped => ForCompletion(completionRatio),
        PlaybackEventType.TrackCompleted => CompletedWeight,
        PlaybackEventType.TrackReplayed => ReplayedWeight,
        PlaybackEventType.TrackLiked => LikedWeight,
        PlaybackEventType.TrackUnliked => UnlikedWeight,
        PlaybackEventType.TrackAddedToPlaylist => PlaylistAddWeight,
        PlaybackEventType.TrackRemovedFromPlaylist => PlaylistRemoveWeight,
        PlaybackEventType.TrackAddedToQueue => QueueAddWeight,

        // Выбрать один результат из списка — пусть небольшое, но настоящее предпочтение.
        PlaybackEventType.SearchResultClicked => EntityInterestWeight,

        // Намерение и heartbeat: записываем, но не оцениваем.
        PlaybackEventType.TrackStarted => 0,
        PlaybackEventType.TrackPlayed => 0,
        PlaybackEventType.TrackPaused => 0,

        _ => 0,
    };

    /// <summary>
    /// Вклад события в исполнителя, альбом или плейлист, который оно называет. Открытие страницы —
    /// слабый сигнал: достаточно настоящий, чтобы разрешить ничью, и слишком слабый, чтобы сам по
    /// себе формировать профиль.
    /// </summary>
    public static double ForEntity(PlaybackEventType type) => type switch
    {
        PlaybackEventType.ArtistOpened => EntityInterestWeight,
        PlaybackEventType.AlbumOpened => EntityInterestWeight,
        PlaybackEventType.PlaylistOpened => EntityInterestWeight,
        PlaybackEventType.SearchResultClicked => EntityInterestWeight,
        _ => 0,
    };

    /// <summary>Истина, когда событие описывает прослушивание, от которого пользователь ушёл.</summary>
    public static bool IsSkip(PlaybackEventType type, double completionRatio) =>
        type == PlaybackEventType.TrackSkipped && completionRatio < 0.20;

    /// <summary>
    /// Доля услышанного трека, зажатая в [0, 1].
    /// Отсутствующая или бессмысленная длительность даёт ноль, а не деление на ноль: неизвестная
    /// длина не должна выдаваться за полное прослушивание.
    /// </summary>
    public static double CompletionRatio(int listenedSeconds, int durationSeconds)
    {
        if (durationSeconds <= 0 || listenedSeconds <= 0)
            return 0;

        return Math.Min(1.0, (double)listenedSeconds / durationSeconds);
    }
}
