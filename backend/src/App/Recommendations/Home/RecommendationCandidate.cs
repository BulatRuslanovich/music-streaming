// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Recommendations.Home;

public static class ReasonKinds
{
    public const string BecauseYouListened = "becauseYouListened";
    public const string PopularWithSimilarTaste = "popularWithSimilarTaste";
    public const string NewFromArtistYouPlay = "newFromArtistYouPlay";
    public const string FromGenreYouLike = "fromGenreYouLike";
    public const string Trending = "trending";
    public const string FreshInLibrary = "freshInLibrary";
    public const string Discovery = "discovery";
    public const string SoundsLike = "soundsLike";
    public const string MatchesYourTaste = "matchesYourTaste";
}

public enum CandidateSource
{
    LovedArtists,
    LovedGenres,
    NewReleases,
    Popular,
    Unheard,
    SharedPlaylists,

    SonicNeighbour,

    TasteVector,
}

[Flags]
public enum CandidateSourceFamily
{
    None = 0,
    Content = 1,
    Collaborative = 2,
    Global = 4,
    Sonic = 8,
}

public static class CandidateSources
{
    public static int Count(CandidateSourceFamily families) =>
        System.Numerics.BitOperations.PopCount((uint)families);
}

public class RecommendationCandidate
{
    public required Guid TrackId { get; init; }
    public required Guid ArtistId { get; init; }
    public Guid? AlbumId { get; init; }
    public Guid? GenreId { get; init; }
    public int? Year { get; init; }
    public IReadOnlyList<Guid> ArtistIds { get; init; } = [];
    public CandidateSource Source { get; set; }
    public double Content { get; set; }

    public double? TasteFit { get; set; }

    public double? AudioSimilarity { get; set; }

    public int EmbeddingRow { get; set; } = -1;
    public double Collaborative { get; set; }
    public double Behavior { get; set; }
    public double Popularity { get; set; }
    public double Freshness { get; set; }
    public double Coverage { get; set; }

    public double? GlobalSkipRate { get; set; }
    public int EvidenceCount { get; set; } = 1;
    public double Score { get; set; }
    public bool IsNovel { get; set; }
    public string ReasonKind { get; set; } = ReasonKinds.Discovery;
    public string? ReasonSubject { get; set; }
    public Guid? ReasonSubjectId { get; set; }

    public RecommendationCandidate WithScore(double score)
    {
        var copy = (RecommendationCandidate)MemberwiseClone();
        copy.Score = score;

        return copy;
    }
}
