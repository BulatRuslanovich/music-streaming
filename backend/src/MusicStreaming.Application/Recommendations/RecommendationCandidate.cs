namespace MusicStreaming.Application.Recommendations;

/// <summary>Почему трек рекомендован. API отдаёт вид причины; формулировку подставляет клиент.</summary>
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
}

/// <summary>Какой генератор выдал кандидата — нужно для выбора причины и для отладки.</summary>
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
}

/// <summary>
/// Рассматриваемый трек вместе со всем, что нужно ранжированию для оценки.
///
/// <para>
/// Изменяемый класс, а не record: на каждую полку оценивается несколько сотен таких объектов, и
/// каждый по очереди проходит генерацию, оценку, штрафы и переранжирование. Копировать их на
/// каждом этапе — чистая трата.
/// </para>
/// </summary>
public class RecommendationCandidate
{
    public required Guid TrackId { get; init; }
    public required Guid ArtistId { get; init; }
    public Guid? AlbumId { get; init; }
    public Guid? GenreId { get; init; }
    public int? Year { get; init; }

    /// <summary>Все указанные исполнители, чтобы квота учитывала и совместные треки.</summary>
    public IReadOnlyList<Guid> ArtistIds { get; init; } = [];

    public CandidateSource Source { get; set; }

    /// <summary>Наибольшая похожесть по метаданным на то, что пользователь уже любит, в [0, 1].</summary>
    public double Content { get; set; }

    /// <summary>Наибольшая похожесть по совстречаемости с тем, что пользователь любит, в [0, 1].</summary>
    public double Collaborative { get; set; }

    /// <summary>Аффинити к исполнителю и жанру кандидата, в [-1, 1].</summary>
    public double Behavior { get; set; }

    public double Popularity { get; set; }
    public double Freshness { get; set; }
    public double Coverage { get; set; }

    /// <summary>Итоговая оценка после взвешивания и штрафов.</summary>
    public double Score { get; set; }

    /// <summary>Истина, когда кандидат лежит за пределами устоявшегося вкуса пользователя.</summary>
    public bool IsNovel { get; set; }

    public string ReasonKind { get; set; } = ReasonKinds.Discovery;
    public string? ReasonSubject { get; set; }
    public Guid? ReasonSubjectId { get; set; }

    /// <summary>Сливает дубликат от другого источника, оставляя самое сильное свидетельство.</summary>
    public void MergeWith(RecommendationCandidate other)
    {
        Content = Math.Max(Content, other.Content);
        Collaborative = Math.Max(Collaborative, other.Collaborative);
        Popularity = Math.Max(Popularity, other.Popularity);
        Freshness = Math.Max(Freshness, other.Freshness);
        Coverage = Math.Max(Coverage, other.Coverage);
        IsNovel &= other.IsNovel;

        // Объяснение остаётся за источником, который первым объяснил кандидата: источники идут в
        // порядке того, насколько убедительно читается их формулировка.
    }
}
