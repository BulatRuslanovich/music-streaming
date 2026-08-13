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
    /// <summary>Трек-кандидат. Одновременно ключ дедупликации: один трек — один кандидат на полку, даже если его нашли несколько источников.</summary>
    public required Guid TrackId { get; init; }

    /// <summary>Основной исполнитель трека — используется квотой разнообразия в <see cref="Scoring.Diversifier"/>, чтобы не отдать полку одному артисту.</summary>
    public required Guid ArtistId { get; init; }

    public Guid? AlbumId { get; init; }

    /// <summary>Жанр трека, если он определён в каталоге; влияет на аффинити и на квоту жанрового разнообразия.</summary>
    public Guid? GenreId { get; init; }

    /// <summary>Год выпуска, если известен — используется <see cref="Scoring.Diversifier"/> для мягкого разброса полки по эпохам.</summary>
    public int? Year { get; init; }

    /// <summary>Все указанные исполнители, чтобы квота учитывала и совместные треки.</summary>
    public IReadOnlyList<Guid> ArtistIds { get; init; } = [];

    /// <summary>Источник, впервые предложивший этого кандидата — определяет, как объясняется рекомендация, и полезен при отладке пайплайна.</summary>
    public CandidateSource Source { get; set; }

    /// <summary>Наибольшая похожесть по метаданным на то, что пользователь уже любит, в [0, 1].</summary>
    public double Content { get; set; }

    /// <summary>Наибольшая похожесть по совстречаемости с тем, что пользователь любит, в [0, 1].</summary>
    public double Collaborative { get; set; }

    /// <summary>Аффинити к исполнителю и жанру кандидата, в [-1, 1].</summary>
    public double Behavior { get; set; }

    /// <summary>Общая популярность трека в библиотеке — сигнал, не зависящий от конкретного пользователя, важен для холодного старта.</summary>
    public double Popularity { get; set; }

    /// <summary>Насколько недавно трек появился в библиотеке, в [0, 1] — питает источник новинок и штраф на старьё.</summary>
    public double Freshness { get; set; }

    /// <summary>Насколько кандидат закрывает пробел в охвате библиотеки пользователем (жанры/исполнители, которые тот ещё не пробовал).</summary>
    public double Coverage { get; set; }

    /// <summary>Итоговая оценка после взвешивания и штрафов.</summary>
    public double Score { get; set; }

    /// <summary>Истина, когда кандидат лежит за пределами устоявшегося вкуса пользователя.</summary>
    public bool IsNovel { get; set; }

    /// <summary>Код причины рекомендации из <see cref="ReasonKinds"/> — API отдаёт его как есть, текст подставляет клиент.</summary>
    public string ReasonKind { get; set; } = ReasonKinds.Discovery;

    /// <summary>Подставляемое в формулировку причины имя — например, имя исполнителя для "новое от Х".</summary>
    public string? ReasonSubject { get; set; }

    /// <summary>Идентификатор объекта из <see cref="ReasonSubject"/>, чтобы клиент мог сделать его кликабельной ссылкой.</summary>
    public Guid? ReasonSubjectId { get; set; }

    /// <summary>
    /// Сливает дубликат от другого источника, оставляя самое сильное свидетельство.
    ///
    /// <para>
    /// Один и тот же трек может предложить несколько источников (например, «похоже на недавнее»
    /// и «популярное у похожих слушателей»); вместо того чтобы оставлять на полке несколько копий
    /// или выбирать источник произвольно, кандидаты объединяются, беря максимум по каждому сигналу —
    /// так трек получает наиболее убедительную оценку из всех, что для него нашлись.
    /// </para>
    /// </summary>
    /// <param name="other">Дубликат того же трека от другого источника, чьи сигналы нужно учесть.</param>
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
