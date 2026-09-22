// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.UnitTests.Recommendations;

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
        IReadOnlyList<Guid>? artistIds = null,
        double? tasteFit = null,
        int embeddingRow = -1) =>
        new()
        {
            TrackId = trackId ?? Guid.CreateVersion7(),
            ArtistId = artistId ?? Guid.CreateVersion7(),
            ArtistIds = artistIds ?? [artistId ?? Guid.CreateVersion7()],
            AlbumId = albumId,
            GenreId = genreId,
            Year = year,
            Score = score,
            IsNovel = novel,
            TasteFit = tasteFit,
            EmbeddingRow = embeddingRow,
        };

    public static List<RecommendationCandidate> SameArtist(int count, Guid artistId)
    {
        var candidates = new List<RecommendationCandidate>(count);

        for (var index = 0; index < count; index++)
            candidates.Add(Candidate(score: 1.0 - index * 0.01, artistId: artistId));

        return candidates;
    }

    public static RecommendationOptions Options() => new();

    // Узкие группы: чистые классы берут только свою, поэтому тест тоже берёт только свою.
    public static CandidatePenaltyOptions Penalties() => new();
    public static DiversityOptions Limits() => new();
    public static ExplorationOptions Exploring() => new();
}
