using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

/// <summary>
/// Выбирает итоговый состав полки.
///
/// <para>
/// Одно только ранжирование выдаёт полку из двенадцати песен одного исполнителя: если человек
/// любит артиста, то каждый его трек получает высокую оценку. Разнообразие — свойство
/// <em>набора</em>, а не отдельного трека, поэтому оно не может быть слагаемым в оценке кандидата;
/// это правило отбора. Здесь — жадный проход maximal marginal relevance: каждый следующий выбор —
/// кандидат с лучшей оценкой за вычетом его сходства с тем, что уже на полке, при жёстких квотах
/// на исполнителя, альбом и жанр.
/// </para>
/// </summary>
public static class Diversifier
{
    /// <summary>
    /// Отбирает до <paramref name="count"/> кандидатов.
    ///
    /// <para>
    /// <paramref name="alreadySelected"/> позволяет второму проходу — исследовательской половине
    /// полки — считаться с теми же квотами, чтобы две половины вместе не набрали четыре трека
    /// одного исполнителя.
    /// </para>
    ///
    /// <para>
    /// С выключенным <paramref name="allowRelaxation"/> проход вернёт меньше запрошенного, но не
    /// нарушит квоту. Это позволяет вызывающему сначала предложить оставшиеся слоты другому пулу и
    /// только потом скатываться к повторам.
    /// </para>
    /// </summary>
    /// <param name="candidates">Пул кандидатов, из которого происходит отбор — сортируется по оценке внутри метода, исходный порядок не важен.</param>
    /// <param name="count">Сколько кандидатов нужно отобрать; итоговый список может быть короче при <paramref name="allowRelaxation"/> = <c>false</c>.</param>
    /// <param name="options">Источник квот (максимум на исполнителя/альбом/жанр) и коэффициента разнообразия <c>DiversityLambda</c>.</param>
    /// <param name="alreadySelected">Уже выбранные в другом проходе кандидаты — учитываются в квотах и в похожести, но не входят в возвращаемый список.</param>
    /// <param name="allowRelaxation">Разрешить ли постепенно ослаблять квоты (сначала по жанру, потом по альбому, в конце по исполнителю), когда пул исчерпан.</param>
    /// <returns>Отобранные кандидаты в порядке выбора; может быть короче <paramref name="count"/>, если пул мал и ослабление квот запрещено или тоже исчерпано.</returns>
    public static List<RecommendationCandidate> Select(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        RecommendationOptions options,
        IReadOnlyList<RecommendationCandidate>? alreadySelected = null,
        bool allowRelaxation = true)
    {
        var selected = new List<RecommendationCandidate>(count);
        if (count <= 0 || candidates.Count == 0)
            return selected;

        var context = new CapContext(options);
        foreach (var previous in alreadySelected ?? [])
            context.Take(previous);

        var pool = candidates.OrderByDescending(c => c.Score).ToList();
        var lambda = options.DiversityLambda;
        var relaxation = CapRelaxation.None;

        while (selected.Count < count && pool.Count > 0)
        {
            var bestIndex = -1;
            var bestValue = double.NegativeInfinity;

            for (var index = 0; index < pool.Count; index++)
            {
                var candidate = pool[index];
                if (!context.Allows(candidate, relaxation))
                    continue;

                var penalty = MaximumSimilarity(candidate, selected, alreadySelected);
                var value = (1 - lambda) * candidate.Score - lambda * penalty;

                if (value > bestValue)
                {
                    bestValue = value;
                    bestIndex = index;
                }
            }

            // Ничего из оставшегося не проходит. Короткая полка хуже однообразной, поэтому квота
            // уступает — но по одной за раз, начиная со слабейшей. Повтор жанра заметен меньше
            // всего, повтор исполнителя — больше всего, так что бедная жанрами библиотека не
            // отнимает ту гарантию, которая действительно важна: не двенадцать песен одного артиста.
            if (bestIndex < 0)
            {
                if (!allowRelaxation || relaxation == CapRelaxation.All)
                    break;

                relaxation++;
                continue;
            }

            var chosen = pool[bestIndex];
            pool.RemoveAt(bestIndex);
            context.Take(chosen);
            selected.Add(chosen);
        }

        return selected;
    }

    /// <summary>
    /// Штраф за сходство с уже отобранным: чем ближе кандидат к тому, что уже на полке, тем менее он желанен, даже при высокой собственной оценке.
    /// </summary>
    /// <returns>Наибольшая похожесть кандидата на любой уже выбранный трек, из текущего прохода или из <paramref name="alreadySelected"/>.</returns>
    private static double MaximumSimilarity(
        RecommendationCandidate candidate,
        List<RecommendationCandidate> selected,
        IReadOnlyList<RecommendationCandidate>? alreadySelected)
    {
        var highest = 0.0;

        foreach (var other in selected)
            highest = Math.Max(highest, MetadataSimilarity(candidate, other));

        foreach (var other in alreadySelected ?? [])
            highest = Math.Max(highest, MetadataSimilarity(candidate, other));

        return highest;
    }

    /// <summary>
    /// Насколько два кандидата похожи для человека, скользящего взглядом по полке.
    ///
    /// <para>
    /// Намеренно прикидка по метаданным, а не обращение к таблице похожести: переранжирование
    /// спрашивает это тысячи раз на полку, а «тот же исполнитель» — именно то, что читатель
    /// замечает, то есть тот самый повтор, ради предотвращения которого диверсификатор и нужен.
    /// </para>
    /// </summary>
    /// <returns>
    /// Оценка похожести в [0, 1]: 1 — тот же трек, 0.9 — тот же альбом, 0.8 — общий исполнитель,
    /// 0.4 — тот же жанр, до 0.2 с экспоненциальным затуханием — близкие года выпуска, 0 — ничего общего.
    /// </returns>
    public static double MetadataSimilarity(RecommendationCandidate left, RecommendationCandidate right)
    {
        if (left.TrackId == right.TrackId)
            return 1.0;

        if (left.AlbumId is not null && left.AlbumId == right.AlbumId)
            return 0.9;

        if (SharesArtist(left, right))
            return 0.8;

        if (left.GenreId is not null && left.GenreId == right.GenreId)
            return 0.4;

        if (left.Year is { } leftYear && right.Year is { } rightYear)
            return 0.2 * Math.Exp(-Math.Abs(leftYear - rightYear) / 10.0);

        return 0;
    }

    /// <summary>Сравнивает не только основных исполнителей, но и весь список соавторов — совместный трек не должен считаться «новым» исполнителем.</summary>
    private static bool SharesArtist(RecommendationCandidate left, RecommendationCandidate right)
    {
        if (left.ArtistId == right.ArtistId)
            return true;

        foreach (var artistId in left.ArtistIds)
        {
            if (artistId == right.ArtistId || right.ArtistIds.Contains(artistId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Сколько квот ещё соблюдается — в порядке, в котором от них отказываются: повтор жанра почти
    /// незаметен, повтор альбома заметен слегка, а повтор исполнителя — именно то, на что
    /// слушатель жалуется.
    /// </summary>
    private enum CapRelaxation
    {
        None = 0,
        WithoutGenre = 1,
        WithoutAlbum = 2,
        All = 3,
    }

    /// <summary>Считает то, что уже на полке, против квот.</summary>
    private sealed class CapContext(RecommendationOptions options)
    {
        private readonly Dictionary<Guid, int> _artists = [];
        private readonly Dictionary<Guid, int> _albums = [];
        private readonly Dictionary<Guid, int> _genres = [];

        /// <summary>Проверяет, не упрётся ли добавление кандидата в квоту исполнителя, альбома или жанра — с учётом того, какие квоты уже ослаблены.</summary>
        public bool Allows(RecommendationCandidate candidate, CapRelaxation relaxation)
        {
            if (relaxation == CapRelaxation.All)
                return true;

            foreach (var artistId in Credits(candidate))
            {
                if (_artists.GetValueOrDefault(artistId) >= options.MaxPerArtist)
                    return false;
            }

            if (relaxation >= CapRelaxation.WithoutAlbum)
                return true;

            if (candidate.AlbumId is { } albumId && _albums.GetValueOrDefault(albumId) >= options.MaxPerAlbum)
                return false;

            if (relaxation >= CapRelaxation.WithoutGenre)
                return true;

            return candidate.GenreId is not { } genreId
                   || _genres.GetValueOrDefault(genreId) < options.MaxPerGenre;
        }

        /// <summary>Учитывает выбранного кандидата в счётчиках квот, чтобы следующая проверка <see cref="Allows"/> видела актуальное состояние полки.</summary>
        public void Take(RecommendationCandidate candidate)
        {
            foreach (var artistId in Credits(candidate))
                _artists[artistId] = _artists.GetValueOrDefault(artistId) + 1;

            if (candidate.AlbumId is { } albumId)
                _albums[albumId] = _albums.GetValueOrDefault(albumId) + 1;

            if (candidate.GenreId is { } genreId)
                _genres[genreId] = _genres.GetValueOrDefault(genreId) + 1;
        }

        /// <summary>Все исполнители, которых квота должна учитывать для этого кандидата — совместный трек считается против каждого соавтора.</summary>
        private static IEnumerable<Guid> Credits(RecommendationCandidate candidate) =>
            candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId];
    }
}
