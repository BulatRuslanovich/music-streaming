// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class Diversifier
{
    public static List<RecommendationCandidate> Select(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        RecommendationOptions options,
        IReadOnlyList<RecommendationCandidate>? alreadySelected = null,
        bool allowRelaxation = true,
        IVectorSimilarity? vectors = null,
        double? diversityLambda = null,
        double? artistRepeatPenalty = null)
    {
        var selected = new List<RecommendationCandidate>(count);
        if (count <= 0 || candidates.Count == 0)
            return selected;

        var context = new CapContext(options);
        foreach (var previous in alreadySelected ?? [])
            context.Take(previous);

        var pool = candidates.OrderByDescending(c => c.Score).ToList();
        var lambda = diversityLambda ?? options.DiversityLambda;
        var repeatPenalty = artistRepeatPenalty ?? options.ArtistRepeatPenalty;
        var relaxation = CapRelaxation.None;

        var penalties = new double[pool.Count];
        foreach (var previous in alreadySelected ?? [])
            Absorb(pool, penalties, previous, vectors);

        while (selected.Count < count && pool.Count > 0)
        {
            var bestIndex = -1;
            var bestValue = double.NegativeInfinity;

            for (var index = 0; index < pool.Count; index++)
            {
                var candidate = pool[index];
                if (!context.Allows(candidate, relaxation))
                    continue;

                // Накопительный штраф за артиста. При MaxPerArtist = 2 он срабатывает от силы
                // один раз и служит тайбрейком — но на последней ступени послаблений лимитов
                // нет вовсе, и тогда он единственное, что мешает добивке склеить хвост полки
                // из треков одного артиста.
                var value = (1 - lambda) * candidate.Score
                            - lambda * penalties[index]
                            - repeatPenalty * context.ArtistsTaken(candidate);

                if (value > bestValue)
                {
                    bestValue = value;
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                if (!allowRelaxation || relaxation == CapRelaxation.All)
                    break;

                relaxation++;
                continue;
            }

            var chosen = pool[bestIndex];

            pool.RemoveAt(bestIndex);
            Array.Copy(penalties, bestIndex + 1, penalties, bestIndex, pool.Count - bestIndex);

            context.Take(chosen);
            selected.Add(chosen);

            Absorb(pool, penalties, chosen, vectors);
        }

        return selected;
    }

    private static void Absorb(
        List<RecommendationCandidate> pool,
        double[] penalties,
        RecommendationCandidate taken,
        IVectorSimilarity? vectors)
    {
        for (var index = 0; index < pool.Count; index++)
            penalties[index] = Math.Max(penalties[index], Similarity(pool[index], taken, vectors));
    }

    /// <summary>
    /// Похожесть двух кандидатов для MMR. Метаданные задают верхние ступени, звучание — нижнюю
    /// границу: два трека, звучащие одинаково, не становятся разнообразием только потому,
    /// что у них разные жанровые ярлыки.
    /// </summary>
    public static double Similarity(
        RecommendationCandidate left,
        RecommendationCandidate right,
        IVectorSimilarity? vectors = null) =>
        Math.Max(MetadataSimilarity(left, right), SonicSimilarity(left, right, vectors));

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

    /// <summary>Ниже этого косинуса CLAP уже не различает — считаем, что общего нет.</summary>
    private const double SonicFloor = 0.35;

    /// <summary>Где косинус считается полным совпадением звучания.</summary>
    private const double SonicSaturation = 0.95;

    /// <summary>
    /// Потолок ниже ступени «тот же альбом» (0.9): звучание — весомый повод разбавить подборку,
    /// но прямое совпадение метаданных всё же сильнее. Потолок выше прежних 0.7, потому что
    /// выученный вектор заслуживает больше доверия, чем совпадение темпа с яркостью.
    /// </summary>
    private const double SonicCeiling = 0.85;

    /// <summary>
    /// Сходство звучания по эмбеддингам. Косинусы CLAP сжаты — 0.6 уже означает «довольно
    /// похоже», — поэтому диапазон растягивается, иначе терм не срабатывал бы никогда.
    /// Обе константы подбираются по <c>make eval</c>, а не на глаз.
    /// </summary>
    public static double SonicSimilarity(
        RecommendationCandidate left,
        RecommendationCandidate right,
        IVectorSimilarity? vectors)
    {
        if (vectors is null || left.EmbeddingRow < 0 || right.EmbeddingRow < 0)
            return 0;

        var cosine = vectors.Between(left.EmbeddingRow, right.EmbeddingRow);
        var scaled = (cosine - SonicFloor) / (SonicSaturation - SonicFloor);

        return Math.Clamp(scaled, 0, 1) * SonicCeiling;
    }

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

    private enum CapRelaxation
    {
        None = 0,
        WithoutGenre = 1,
        WithoutAlbum = 2,
        All = 3,
    }

    private sealed class CapContext(RecommendationOptions options)
    {
        private readonly Dictionary<Guid, int> _artists = [];
        private readonly Dictionary<Guid, int> _albums = [];
        private readonly Dictionary<Guid, int> _genres = [];

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

        /// <summary>Сколько раз артисты этого кандидата уже встречались в подборке.</summary>
        public int ArtistsTaken(RecommendationCandidate candidate)
        {
            var taken = 0;
            foreach (var artistId in Credits(candidate))
                taken = Math.Max(taken, _artists.GetValueOrDefault(artistId));

            return taken;
        }

        public void Take(RecommendationCandidate candidate)
        {
            foreach (var artistId in Credits(candidate))
                _artists[artistId] = _artists.GetValueOrDefault(artistId) + 1;

            if (candidate.AlbumId is { } albumId)
                _albums[albumId] = _albums.GetValueOrDefault(albumId) + 1;

            if (candidate.GenreId is { } genreId)
                _genres[genreId] = _genres.GetValueOrDefault(genreId) + 1;
        }

        private static IEnumerable<Guid> Credits(RecommendationCandidate candidate) =>
            candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId];
    }
}
