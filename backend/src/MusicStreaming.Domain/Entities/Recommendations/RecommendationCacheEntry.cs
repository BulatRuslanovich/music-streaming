namespace MusicStreaming.Domain.Entities.Recommendations;

public record CachedRecommendation(
    Guid ItemId,
    RecommendedItemKind Kind,
    double Score,
    string ReasonKind,
    string? ReasonSubject,
    Guid? ReasonSubjectId);

public class RecommendationCacheEntry
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ShelfKey { get; set; } = string.Empty;
    public int Position { get; set; }
    public IReadOnlyList<CachedRecommendation> Payload { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid RunId { get; set; }
}

public class RecommendationImpression
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public string ShelfKey { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTimeOffset ShownAt { get; set; }
    public DateTimeOffset? ClickedAt { get; set; }
}

public class RecommendationRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? UserId { get; set; }
    public RecommendationTrigger Trigger { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int DurationMs { get; set; }
    public int CandidateCount { get; set; }
    public int ShelfCount { get; set; }
    public RecommendationRunStatus Status { get; set; }
    public string? Error { get; set; }
}
