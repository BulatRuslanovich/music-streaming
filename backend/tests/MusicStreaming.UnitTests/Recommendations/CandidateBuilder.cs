using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.UnitTests.Recommendations;

/// <summary>
/// Собирает кандидатов для тестов ранжирования и отбора, чтобы каждый тест заявлял только то
/// свойство, о котором он на самом деле.
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
        Guid? trackId = null) =>
        new()
        {
            TrackId = trackId ?? Guid.CreateVersion7(),
            ArtistId = artistId ?? Guid.CreateVersion7(),
            ArtistIds = [artistId ?? Guid.CreateVersion7()],
            AlbumId = albumId,
            GenreId = genreId,
            Year = year,
            Score = score,
            IsNovel = novel,
        };

    /// <summary>Череда кандидатов одного исполнителя — повтор, которого полка показывать не должна.</summary>
    public static List<RecommendationCandidate> SameArtist(int count, Guid artistId)
    {
        var candidates = new List<RecommendationCandidate>(count);

        for (var index = 0; index < count; index++)
            candidates.Add(Candidate(score: 1.0 - index * 0.01, artistId: artistId));

        return candidates;
    }

    public static RecommendationOptions Options() => new();
}
