namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Все поведенческие сигналы, которые может сообщить клиент.
///
/// Числовые значения хранятся в базе, поэтому их нельзя переупорядочивать или переиспользовать —
/// только дописывать новые.
/// </summary>
public enum PlaybackEventType
{
    /// <summary>Значение по умолчанию для нераспознанного или отсутствующего типа — такие события отбрасываются на входе, а не сохраняются.</summary>
    Unknown = 0,

    /// <summary>Трек поставлен на воспроизведение.</summary>
    TrackStarted = 1,

    /// <summary>Периодический heartbeat с накопленными секундами реального прослушивания.</summary>
    TrackPlayed = 2,

    /// <summary>Трек дослушан до конца — самый сильный положительный сигнал.</summary>
    TrackCompleted = 3,

    /// <summary>Трек брошен до конца; знак вклада в аффинити зависит от того, на какой доле это произошло.</summary>
    TrackSkipped = 4,

    /// <summary>Воспроизведение поставлено на паузу.</summary>
    TrackPaused = 5,

    /// <summary>Трек запущен повторно в той же сессии.</summary>
    TrackReplayed = 6,

    /// <summary>Пользователь поставил треку лайк.</summary>
    TrackLiked = 7,

    /// <summary>Пользователь снял лайк с трека.</summary>
    TrackUnliked = 8,

    /// <summary>Трек добавлен в плейлист.</summary>
    TrackAddedToPlaylist = 9,

    /// <summary>Трек удалён из плейлиста.</summary>
    TrackRemovedFromPlaylist = 10,

    /// <summary>Трек добавлен в очередь воспроизведения.</summary>
    TrackAddedToQueue = 11,

    /// <summary>Открыта страница исполнителя.</summary>
    ArtistOpened = 12,

    /// <summary>Открыта страница альбома.</summary>
    AlbumOpened = 13,

    /// <summary>Пользователь кликнул по результату поиска.</summary>
    SearchResultClicked = 14,

    /// <summary>Открыта страница плейлиста.</summary>
    PlaylistOpened = 15,
}

/// <summary>
/// Откуда запущено воспроизведение. Значения хранятся в базе, поэтому список только дополняется.
/// </summary>
public enum PlaybackSource
{
    Unknown = 0,
    Home = 1,

    /// <summary>Запущено с полки рекомендаций — только такие прослушивания учитываются в CTR и метриках движка.</summary>
    Recommendation = 2,

    Search = 3,
    Album = 4,
    Artist = 5,
    Playlist = 6,
    Favorites = 7,
    Genre = 8,
    History = 9,
    Queue = 10,
    Tracks = 11,
    Radio = 12,
}

/// <summary>
/// Насколько движок может доверять профилю пользователя — от этого зависит набор весов ранжирования.
/// </summary>
public enum ProfileMaturity
{
    /// <summary>Положительных сигналов нет вовсе — ранжирование опирается на популярность, новизну и охват библиотеки.</summary>
    Cold = 0,

    /// <summary>Сигнала достаточно, чтобы понять общую форму вкуса, но недостаточно, чтобы доверять коллаборативной фильтрации.</summary>
    Warm = 1,

    /// <summary>Сигнала достаточно, чтобы совстречаемость несла наибольший вес среди компонентов оценки.</summary>
    Mature = 2,
}

/// <summary>Чем вызван проход генерации полок — записывается в <see cref="RecommendationRun"/> для отладки.</summary>
public enum RecommendationTrigger
{
    /// <summary>Плановый фоновый проход по расписанию воркера обслуживания библиотеки.</summary>
    Scheduled = 0,

    /// <summary>Вызван активностью пользователя после дебаунса.</summary>
    Activity = 1,

    /// <summary>Построен синхронно во время запроса — первый визит пользователя или пустой кэш.</summary>
    OnDemand = 2,
}

/// <summary>Итог одного прохода генерации полок.</summary>
public enum RecommendationRunStatus
{
    Succeeded = 0,

    /// <summary>Проход упал с исключением — подробности в <see cref="RecommendationRun.Error"/>.</summary>
    Failed = 1,
}

/// <summary>На что ссылается слот в закэшированной полке.</summary>
public enum RecommendedItemKind
{
    Track = 0,

    /// <summary>Полка агрегирована до исполнителей, а не отдельных треков (например, "исполнители для вас").</summary>
    Artist = 1,

    /// <summary>Полка агрегирована до альбомов.</summary>
    Album = 2,
}
