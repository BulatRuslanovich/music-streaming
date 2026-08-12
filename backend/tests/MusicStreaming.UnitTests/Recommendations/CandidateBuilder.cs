using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.UnitTests.Recommendations;

/// <summary>
/// Builds candidates for the ranking and selection tests, so each test states only the property
/// it is actually about.
/// </summary>
internal static class CandidateBuilder
{
    public static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    public static RecommendationCandidate Candidate(
        double score = 0.5,
        Guid? artistId = null,
        Guid? albumId = null,
        Guid? genreId = null,
        int? year = null,
        bool novel = false,
        Guid? trackId = null,
        DateTimeOffset? createdAt = null) =>
        new()
        {
            TrackId = trackId ?? Guid.CreateVersion7(),
            ArtistId = artistId ?? Guid.CreateVersion7(),
            ArtistIds = [artistId ?? Guid.CreateVersion7()],
            AlbumId = albumId,
            GenreId = genreId,
            Year = year,
            CreatedAt = createdAt ?? Now,
            Score = score,
            IsNovel = novel,
        };

    /// <summary>A run of candidates by the same artist — the repetition a shelf must not show.</summary>
    public static List<RecommendationCandidate> SameArtist(int count, Guid artistId)
    {
        var candidates = new List<RecommendationCandidate>(count);

        for (var index = 0; index < count; index++)
            candidates.Add(Candidate(score: 1.0 - index * 0.01, artistId: artistId));

        return candidates;
    }

    public static RecommendationOptions Options() => new();
}
