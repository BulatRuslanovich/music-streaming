namespace MusicStreaming.Application.Dtos;

public enum HomeBlockLayout
{
    Shelf,

    Hero,

    Tile,

    QuickTiles,

    Grid,

    Chart,

    Circles,
}

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


public enum HomeMixKind
{
    Daily,
    New,
    Top,
}

public record HomeMixDto(HomeMixKind Kind, IReadOnlyList<TrackDto> Tracks);

public record HomeFeedDto(
    IReadOnlyList<HomeBlockDto> Blocks,
    LibraryStatsDto Stats,
    bool IsColdStart,
    DateTimeOffset? GeneratedAt);
