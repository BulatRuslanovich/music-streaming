// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

public static class ReasonKinds
{
    public const string BecauseYouListened = "becauseYouListened";
    public const string SimilarTo = "similarTo";
    public const string PopularWithSimilarTaste = "popularWithSimilarTaste";
    public const string NewFromArtistYouPlay = "newFromArtistYouPlay";
    public const string FromGenreYouLike = "fromGenreYouLike";
    public const string Trending = "trending";
    public const string FreshInLibrary = "freshInLibrary";
    public const string ContinueListening = "continueListening";
    public const string Discovery = "discovery";
    public const string Rediscovery = "rediscovery";
}

public enum CandidateSource
{
    SimilarToRecent,
    LovedArtists,
    SimilarListeners,
    LovedGenres,
    NewReleases,
    Popular,
    Unheard,
    SharedPlaylists,
    ContinueListening,
    Rediscovery,
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
    public double Collaborative { get; set; }
    public double Behavior { get; set; }
    public double Popularity { get; set; }
    public double Freshness { get; set; }
    public double Coverage { get; set; }
    public double Score { get; set; }
    public bool IsNovel { get; set; }
    public string ReasonKind { get; set; } = ReasonKinds.Discovery;
    public string? ReasonSubject { get; set; }
    public Guid? ReasonSubjectId { get; set; }
}
