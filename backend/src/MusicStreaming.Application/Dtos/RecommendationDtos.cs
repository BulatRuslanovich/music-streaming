namespace MusicStreaming.Application.Dtos;

public record RecommendationReasonDto(string Kind, string? Subject, Guid? SubjectId);

public record RecommendedTrackDto(TrackDto Track, RecommendationReasonDto Reason, double? Score);

public record RecommendationSectionDto(
    string Key,
    string BaseKey,
    RecommendationReasonDto? Reason,
    IReadOnlyList<RecommendedTrackDto>? Tracks,
    IReadOnlyList<ArtistDto>? Artists,
    IReadOnlyList<AlbumDto>? Albums);

public record RecommendationHomeDto(
    IReadOnlyList<RecommendationSectionDto> Sections,
    bool IsColdStart,
    DateTimeOffset? GeneratedAt);

public record RadioRequest(Guid? SeedTrackId, IReadOnlyList<Guid>? Exclude, int? Limit);

public record RadioBatchDto(IReadOnlyList<RecommendedTrackDto> Tracks, Guid? SeedTrackId)
{
    public static readonly RadioBatchDto Empty = new([], null);
}
