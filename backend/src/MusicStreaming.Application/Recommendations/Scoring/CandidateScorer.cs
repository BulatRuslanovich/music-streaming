using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Всё, что ранкер знает о пользователе в момент оценки кандидатов. Данные загружены заранее,
/// поэтому сам расчёт не обращается к базе и тестируется напрямую.
/// </summary>
/// <param name="ArtistScores">Аффинити пользователя к исполнителям, в [-1, 1], ключ — идентификатор исполнителя.</param>
/// <param name="GenreScores">Аффинити пользователя к жанрам, в [-1, 1], ключ — идентификатор жанра.</param>
/// <param name="History">История прослушиваний пользователя по трекам, использованная для штрафов за «приелось».</param>
/// <param name="LastShown">Когда трек последним показывался пользователю на любой полке — для штрафа за непросмотренные показы.</param>
/// <param name="Now">Момент, относительно которого считаются штрафы за недавность.</param>
public record RankingContext(
    IReadOnlyDictionary<Guid, double> ArtistScores,
    IReadOnlyDictionary<Guid, double> GenreScores,
    IReadOnlyDictionary<Guid, TrackHistory> History,
    IReadOnlyDictionary<Guid, DateTimeOffset> LastShown,
    DateTimeOffset Now)
{
    /// <summary>Контекст холодного старта — пустые словари сигналов, так как у пользователя ещё нет истории.</summary>
    public static RankingContext Empty(DateTimeOffset now) =>
        new(new Dictionary<Guid, double>(), new Dictionary<Guid, double>(),
            new Dictionary<Guid, TrackHistory>(), new Dictionary<Guid, DateTimeOffset>(), now);
}

/// <summary>Что пользователь уже делал с треком.</summary>
/// <param name="LastPlayedAt">Момент последнего прослушивания — используется для штрафа «только что играло».</param>
/// <param name="PlayCount">Сколько раз трек был запущен.</param>
/// <param name="SkipCount">Сколько раз трек был брошен вскоре после начала.</param>
/// <param name="AverageCompletion">Средняя доля трека, которая обычно дослушивается, в [0, 1].</param>
/// <param name="Score">Итоговая аффинити-оценка трека, посчитанная ролл-апом профиля.</param>
public record TrackHistory(
    DateTimeOffset LastPlayedAt,
    int PlayCount,
    int SkipCount,
    double AverageCompletion,
    double Score);

/// <summary>
/// Сводит компоненты кандидата в одно сравнимое число.
///
/// <para>
/// Два этапа, намеренно разделённые. Взвешенная сумма говорит, насколько кандидат подходит
/// пользователю; штрафы затем говорят, стоит ли показывать его <em>именно сейчас</em>. Второй
/// этап мультипликативен, и поэтому только что прослушанный трек оказывается внизу независимо от
/// того, насколько хорошо он подходит, — аддитивная схема такого гарантировать не может.
/// </para>
/// </summary>
public static class CandidateScorer
{
    /// <summary>
    /// Считает поведенческий сигнал, взвешивает все компоненты и применяет штрафы за недавность —
    /// записывает итог прямо в <paramref name="candidate"/>.<see cref="RecommendationCandidate.Score"/>.
    /// </summary>
    /// <param name="candidate">Кандидат, чьи компоненты (Content, Collaborative, Popularity, Freshness, Coverage) уже заполнены и который получит итоговую оценку.</param>
    /// <param name="context">Сигналы пользователя и история, нужные для поведенческой оценки и штрафов.</param>
    /// <param name="weights">Набор весов, каким компонентам доверять больше — зависит от зрелости профиля.</param>
    /// <param name="options">Настраиваемые пороги и величины штрафов.</param>
    public static void Score(
        RecommendationCandidate candidate,
        RankingContext context,
        RankingWeights weights,
        RecommendationOptions options)
    {
        candidate.Behavior = BehaviorScore(candidate, context);

        var merit = weights.Combine(
            candidate.Content,
            candidate.Collaborative,
            candidate.Behavior,
            candidate.Popularity,
            candidate.Freshness,
            candidate.Coverage);

        candidate.Score = merit * PenaltyFor(candidate, context, options);
    }

    /// <summary>
    /// Аффинити к тому, кто сделал трек, и к тому, что это за трек. Исполнитель весит больше
    /// жанра: «ещё одна песня того, кого вы слушаете» — куда лучшая ставка, чем «ещё одна песня
    /// такого рода, какой вы слушаете».
    /// </summary>
    /// <param name="candidate">Кандидат, чьи исполнитель(и) и жанр сравниваются с аффинити пользователя.</param>
    /// <param name="context">Источник аффинити-словарей по исполнителям и жанрам.</param>
    /// <returns>Взвешенная сумма аффинити к исполнителю (70%) и жанру (30%), зажатая в [-1, 1].</returns>
    public static double BehaviorScore(RecommendationCandidate candidate, RankingContext context)
    {
        var artist = 0.0;
        foreach (var artistId in candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId])
        {
            if (context.ArtistScores.TryGetValue(artistId, out var score))
                artist = Math.Abs(score) > Math.Abs(artist) ? score : artist;
        }

        var genre = candidate.GenreId is { } genreId && context.GenreScores.TryGetValue(genreId, out var g)
            ? g
            : 0;

        return Math.Clamp(artist * 0.7 + genre * 0.3, -1, 1);
    }

    /// <summary>
    /// Множитель в (0, 1], выражающий, насколько кандидат «приелся».
    ///
    /// <para>
    /// Именно это не даёт движку вечно рекомендовать одни и те же двадцать треков: всё, что только
    /// что играло, уже показывалось и было проигнорировано или прямо не нравится, уходит далеко
    /// вниз — и следующий эшелон кандидатов всплывает сам.
    /// </para>
    /// </summary>
    /// <param name="candidate">Кандидат, чей штраф считается; используется его <see cref="RecommendationCandidate.TrackId"/> и уже посчитанный <see cref="RecommendationCandidate.Behavior"/>.</param>
    /// <param name="context">История прослушиваний и показов, на основе которой считаются штрафы.</param>
    /// <param name="options">Настраиваемые пороги (сколько часов/дней считать «недавним») и величины штрафов.</param>
    /// <returns>
    /// Произведение применимых штрафов в (0, 1] — 1, если ни один штраф не сработал. Штрафы
    /// перемножаются, а не складываются, поэтому несколько причин усиливают друг друга, а не
    /// компенсируются.
    /// </returns>
    public static double PenaltyFor(
        RecommendationCandidate candidate,
        RankingContext context,
        RecommendationOptions options)
    {
        var penalty = 1.0;

        if (context.History.TryGetValue(candidate.TrackId, out var history))
        {
            var sinceLastPlay = context.Now - history.LastPlayedAt;

            if (sinceLastPlay < TimeSpan.FromHours(options.JustPlayedHours))
                penalty *= options.JustPlayedPenalty;
            else if (sinceLastPlay < TimeSpan.FromDays(options.RecentlyPlayedDays))
                penalty *= options.RecentlyPlayedPenalty;

            // Пробовал хотя бы дважды и оба раза бросил — это ответ, а не совпадение.
            if (history is { SkipCount: >= 2, AverageCompletion: < 0.2 })
                penalty *= options.DislikedTrackPenalty;
        }

        if (context.LastShown.TryGetValue(candidate.TrackId, out var shownAt)
            && context.Now - shownAt < TimeSpan.FromDays(options.ImpressionCooldownDays))
        {
            penalty *= options.UnclickedImpressionPenalty;
        }

        if (candidate.Behavior < -0.3)
            penalty *= options.DislikedArtistPenalty;

        return penalty;
    }
}
