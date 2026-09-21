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
    public const string SoundsLike = "soundsLike";
    public const string MatchesYourTaste = "matchesYourTaste";
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

    /// <summary>Сосед по звучанию: косинус к треку, который слушатель только что играл.</summary>
    SonicNeighbour,

    /// <summary>Просто близко к вектору вкуса — самое слабое объяснение из всех.</summary>
    TasteVector,
}

/// <summary>
/// Семейство источников. Мультиисточниковый бонус считается по числу независимых семейств,
/// а не по числу сработавших источников: SimilarToRecent, LovedArtists и LovedGenres опираются
/// на одну и ту же историю прослушиваний и подтверждают друг друга лишь формально.
/// <para>
/// Эмбеддинг вынесен в отдельное семейство осознанно: он не знает ни тегов, ни кредитов, ни
/// того, кто что слушал. Когда трек назвали и по звучанию, и по метаданным — это два разных
/// свидетельства, а не одно, повторённое дважды.
/// </para>
/// </summary>
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

        CandidateSource.SonicNeighbour => CandidateSourceFamily.Sonic,
        CandidateSource.TasteVector => CandidateSourceFamily.Sonic,

        _ => CandidateSourceFamily.Global,
    };

    public static int Count(CandidateSourceFamily families) =>
        System.Numerics.BitOperations.PopCount((uint)families);
}

/// <summary>Скалярные аудио-характеристики: настроение и энергия полок, но не схожесть треков.</summary>
public readonly record struct TrackAudioProfile(double? TempoBpm, double Energy);

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

    /// <summary>
    /// Перцентиль косинуса к вектору вкуса среди всей библиотеки, 0..1. Не сырой косинус:
    /// CLAP-косинусы между музыкальными треками занимают узкую полосу, зависящую от библиотеки,
    /// и сырое значение сделало бы вес непереносимым между установками. null — эмбеддинга нет.
    /// </summary>
    public double? TasteFit { get; set; }

    /// <summary>Косинус к сидам: «похоже на то, что вы слушали». null — эмбеддинга нет.</summary>
    public double? AudioSimilarity { get; set; }

    /// <summary>Строка в матрице эмбеддингов или -1. Нужна MMR, чтобы не копировать вектор в кандидата.</summary>
    public int EmbeddingRow { get; set; } = -1;
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
