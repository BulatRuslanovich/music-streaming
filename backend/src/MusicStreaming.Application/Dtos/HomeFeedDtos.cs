namespace MusicStreaming.Application.Dtos;

public enum HomeBlockLayout
{
    /// <summary>Горизонтальная лента квадратных карточек.</summary>
    Shelf,

    /// <summary>Крупный блок во всю ширину с обложкой, кнопкой воспроизведения и превью треков.</summary>
    Hero,

    /// <summary>Одиночная плитка-ярлык с мозаикой обложек и счётчиком.</summary>
    Tile,

    /// <summary>Ряд широких плиток: обложка слева, название справа.</summary>
    QuickTiles,

    /// <summary>Сетка карточек, где первая крупнее остальных.</summary>
    Grid,

    /// <summary>Нумерованный компактный список.</summary>
    Chart,

    /// <summary>Лента круглых карточек исполнителей.</summary>
    Circles,
}

/// <summary>Один блок главной страницы.</summary>
/// <param name="Key">Уникальный ключ блока в пределах ленты.</param>
/// <param name="BaseKey">Смысл блока: по нему клиент выбирает заголовок и ссылку «смотреть всё».</param>
/// <param name="Layout">Раскладка, которой блок следует нарисовать.</param>
/// <param name="Reason">Пояснение, откуда взялась подборка; заполнено только у рекомендательных блоков.</param>
/// <param name="TotalCount">Полный размер коллекции, когда в блок попала лишь её часть.</param>
public record HomeBlockDto(
    string Key,
    string BaseKey,
    HomeBlockLayout Layout,
    RecommendationReasonDto? Reason,
    IReadOnlyList<TrackDto>? Tracks,
    IReadOnlyList<AlbumDto>? Albums,
    IReadOnlyList<ArtistDto>? Artists,
    IReadOnlyList<PlaylistDto>? Playlists,
    int? TotalCount);

/// <summary>Готовая лента блоков для главной страницы.</summary>
/// <param name="IsColdStart">Признак того, что персональных данных о пользователе ещё нет.</param>
/// <param name="GeneratedAt">Когда пересчитывались рекомендации, лёгшие в основу ленты.</param>
public record HomeFeedDto(
    IReadOnlyList<HomeBlockDto> Blocks,
    LibraryStatsDto Stats,
    bool IsColdStart,
    DateTimeOffset? GeneratedAt);
