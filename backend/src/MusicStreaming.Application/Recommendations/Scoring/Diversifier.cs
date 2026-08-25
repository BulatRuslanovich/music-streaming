// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Recommendations.Scoring;

public static class Diversifier
{
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

        var penalties = new double[pool.Count];
        foreach (var previous in alreadySelected ?? [])
            Absorb(pool, penalties, previous);

        while (selected.Count < count && pool.Count > 0)
        {
            var bestIndex = -1;
            var bestValue = double.NegativeInfinity;

            for (var index = 0; index < pool.Count; index++)
            {
                var candidate = pool[index];
                if (!context.Allows(candidate, relaxation))
                    continue;

                var value = (1 - lambda) * candidate.Score - lambda * penalties[index];

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

            Absorb(pool, penalties, chosen);
        }

        return selected;
    }

    private static void Absorb(
        List<RecommendationCandidate> pool, double[] penalties, RecommendationCandidate taken)
    {
        for (var index = 0; index < pool.Count; index++)
            penalties[index] = Math.Max(penalties[index], Similarity(pool[index], taken));
    }

    /// <summary>
    /// Похожесть двух кандидатов для MMR. Метаданные задают верхние ступени, звук — нижнюю границу:
    /// два трека одного темпа, энергии и тембра не должны считаться разнообразием только потому,
    /// что у них разные жанровые ярлыки.
    /// </summary>
    public static double Similarity(RecommendationCandidate left, RecommendationCandidate right) =>
        Math.Max(MetadataSimilarity(left, right), AudioSimilarity(left, right));

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

    /// <summary>
    /// Потолок 0.7 — ниже ступени «тот же артист»: сходство звучания это повод разбавить подборку,
    /// но не такой сильный, как прямое совпадение метаданных.
    /// </summary>
    public static double AudioSimilarity(RecommendationCandidate left, RecommendationCandidate right)
    {
        if (left.AudioProfile is not { } first || right.AudioProfile is not { } second)
            return 0;

        var tempo = first.TempoBpm is { } leftTempo and > 0 && second.TempoBpm is { } rightTempo and > 0
            ? Math.Exp(-Math.Abs(Math.Log(leftTempo / rightTempo)) / 0.18)
            : 0.5;

        var energy = Math.Exp(-Math.Abs(first.Energy - second.Energy) / 0.18);
        var brightness = Math.Exp(-Math.Abs(first.Brightness - second.Brightness) / 0.18);

        return 0.7 * (0.45 * tempo + 0.35 * energy + 0.20 * brightness);
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
