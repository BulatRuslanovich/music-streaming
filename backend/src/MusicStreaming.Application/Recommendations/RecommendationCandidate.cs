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
    public const string DeepCut = "deepCut";
}

public enum CandidateSource
{
    SimilarToRecent,
    LovedArtists,
    SimilarArtists,
    SimilarListeners,
    LovedGenres,
    NewReleases,
    Popular,
    Unheard,
    SharedPlaylists,
    ContinueListening,
    Rediscovery,
}

/// <summary>
/// Семейство источников. Мультиисточниковый бонус считается по числу независимых семейств,
/// а не по числу сработавших источников: SimilarToRecent, LovedArtists и LovedGenres опираются
/// на одну и ту же историю прослушиваний и подтверждают друг друга лишь формально.
/// </summary>
[Flags]
public enum CandidateSourceFamily
{
    None = 0,
    Content = 1,
    Collaborative = 2,
    Global = 4,
}

public static class CandidateSources
{
    public static CandidateSourceFamily FamilyOf(CandidateSource source) => source switch
    {
        CandidateSource.SimilarToRecent => CandidateSourceFamily.Content,
        CandidateSource.LovedArtists => CandidateSourceFamily.Content,
        CandidateSource.SimilarArtists => CandidateSourceFamily.Content,
        CandidateSource.LovedGenres => CandidateSourceFamily.Content,
        CandidateSource.ContinueListening => CandidateSourceFamily.Content,
        CandidateSource.Rediscovery => CandidateSourceFamily.Content,

        CandidateSource.SimilarListeners => CandidateSourceFamily.Collaborative,
        CandidateSource.SharedPlaylists => CandidateSourceFamily.Collaborative,

        _ => CandidateSourceFamily.Global,
    };

    public static int Count(CandidateSourceFamily families) =>
        System.Numerics.BitOperations.PopCount((uint)families);
}

/// <summary>Скалярные аудио-характеристики трека, нужные для оценки разнообразия подборки.</summary>
public readonly record struct TrackAudioProfile(double? TempoBpm, double Energy, double Brightness);

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
    public double? AudioSimilarity { get; set; }
    public double Collaborative { get; set; }
    public double Behavior { get; set; }
    public double Popularity { get; set; }
    public double Freshness { get; set; }
    public double Coverage { get; set; }
    public TrackAudioProfile? AudioProfile { get; set; }

    /// <summary>Доля пропусков по всей библиотеке. null, когда прослушиваний слишком мало.</summary>
    public double? GlobalSkipRate { get; set; }
    public int EvidenceCount { get; set; } = 1;
    public double Score { get; set; }
    public bool IsNovel { get; set; }
    public string ReasonKind { get; set; } = ReasonKinds.Discovery;
    public string? ReasonSubject { get; set; }
    public Guid? ReasonSubjectId { get; set; }

    /// <summary>
    /// Копия с другим скором. Полке части суток нужен свой порядок, а общий пул трогать нельзя:
    /// из него собираются и все остальные полки.
    /// </summary>
    public RecommendationCandidate WithScore(double score)
    {
        var copy = (RecommendationCandidate)MemberwiseClone();
        copy.Score = score;

        return copy;
    }
}
