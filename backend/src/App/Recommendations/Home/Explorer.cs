// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Recommendations.Embeddings;

namespace App.Recommendations.Home;

public static class Explorer
{
    private const double FarDiversityLambda = 0.55;

    private const double FarArtistRepeatPenalty = 0.20;

    public static List<RecommendationCandidate> Compose(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        double explorationRatio,
        int seed,
        EmbeddingSnapshot? vectors = null)
    {
        if (count <= 0 || candidates.Count == 0)
            return [];

        // Далёкие от вкуса кандидаты (нижний квантиль близости) идут в разведку со своим счётом —
        // чем дальше, тем выше, с детерминированным дрожанием. Без эмбеддингов «далёкие» = новые.
        var far = new List<RecommendationCandidate>();
        var near = new List<RecommendationCandidate>();

        var fits = candidates
            .Where(candidate => candidate.TasteFit is not null)
            .Select(candidate => (float)candidate.TasteFit!.Value)
            .ToArray();

        if (fits.Length == 0)
        {
            foreach (var candidate in candidates)
                (candidate.IsNovel ? far : near).Add(candidate);
        }
        else
        {
            var threshold = VectorMath.Quantile(fits, RecommendationTuning.Exploration.FarQuantile);
            var random = new Random(seed);

            foreach (var candidate in candidates)
            {
                if (candidate.TasteFit is { } fit && fit <= threshold)
                    far.Add(candidate.WithScore(-fit + random.NextDouble() * RecommendationTuning.Exploration.FarJitter));
                else
                    near.Add(candidate);
            }
        }

        var wantedExplore = Math.Min((int)Math.Ceiling(count * explorationRatio), far.Count);
        var exploitSlots = Math.Min(count - wantedExplore, near.Count);

        var exploit = Diversifier.Select(near, exploitSlots, null, allowRelaxation: false, vectors);

        var explore = Diversifier.Select(far,
            count - exploit.Count,
            exploit,
            allowRelaxation: false,
            vectors,
            diversityLambda: FarDiversityLambda,
            artistRepeatPenalty: FarArtistRepeatPenalty);

        // Не хватило — добираем из всех оставшихся с ослаблением лимитов.
        var chosen = exploit.Concat(explore).ToList();
        if (chosen.Count < count)
        {
            var taken = chosen.Select(c => c.TrackId).ToHashSet();
            var remaining = candidates.Where(c => !taken.Contains(c.TrackId)).ToList();

            exploit.AddRange(Diversifier.Select(remaining, count - chosen.Count, chosen, true, vectors));
        }

        // Разведка расставляется по позициям с равным шагом и сдвигом от seed.
        if (explore.Count == 0)
            return exploit;

        if (exploit.Count == 0)
            return explore;

        var total = exploit.Count + explore.Count;
        var result = new List<RecommendationCandidate>(total);

        var stride = (double)total / explore.Count;
        var offset = seed % Math.Max(1, (int)Math.Ceiling(stride));

        var novelPositions = new HashSet<int>();
        for (var index = 0; index < explore.Count; index++)
        {
            var position = Math.Clamp((int)(index * stride) + offset, 1, total - 1);
            novelPositions.Add(position);
        }

        var exploitQueue = new Queue<RecommendationCandidate>(exploit);
        var exploreQueue = new Queue<RecommendationCandidate>(explore);

        for (var position = 0; position < total; position++)
        {
            var wantsNovel = novelPositions.Contains(position) && exploreQueue.Count > 0;

            if (wantsNovel || exploitQueue.Count == 0)
                result.Add(exploreQueue.Count > 0 ? exploreQueue.Dequeue() : exploitQueue.Dequeue());
            else
                result.Add(exploitQueue.Dequeue());
        }

        return result;
    }

    public static int SeedFor(Guid userId, string shelfKey, DateTimeOffset now)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = userId.ToByteArray().Aggregate(offsetBasis, (current, b) => (current ^ b) * prime);

        hash = shelfKey.Aggregate(hash, (current, c) => (current ^ (byte)c) * prime);

        hash = BitConverter.GetBytes(now.UtcDateTime.Date.Ticks).Aggregate(hash, (current, b) => (current ^ b) * prime);

        return (int)(hash & int.MaxValue);
    }
}

public static class Diversifier
{
    private const double SonicFloor = 0.35;

    private const double SonicSaturation = 0.95;

    private const double SonicCeiling = 0.85;

    // Жадный MMR: счёт кандидата минус максимальное сходство с уже выбранными и штраф за повтор артиста.
    // Лимиты на артиста/альбом/жанр ослабляются по очереди, если иначе полку не набрать.
    public static List<RecommendationCandidate> Select(
        IReadOnlyList<RecommendationCandidate> candidates,
        int count,
        IReadOnlyList<RecommendationCandidate>? alreadySelected = null,
        bool allowRelaxation = true,
        EmbeddingSnapshot? vectors = null,
        double? diversityLambda = null,
        double? artistRepeatPenalty = null)
    {
        var selected = new List<RecommendationCandidate>(count);
        if (count <= 0 || candidates.Count == 0)
            return selected;

        var artists = new Dictionary<Guid, int>();
        var albums = new Dictionary<Guid, int>();
        var genres = new Dictionary<Guid, int>();

        foreach (var previous in alreadySelected ?? [])
            Take(previous);

        var pool = candidates.OrderByDescending(c => c.Score).ToList();
        var lambda = diversityLambda ?? RecommendationTuning.Diversity.DiversityLambda;
        var repeatPenalty = artistRepeatPenalty ?? RecommendationTuning.Diversity.ArtistRepeatPenalty;
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

                var allowed = relaxation == CapRelaxation.All
                              || (!Credits(candidate).Any(id => artists.GetValueOrDefault(id) >= RecommendationTuning.Diversity.MaxPerArtist)
                                  && (relaxation >= CapRelaxation.WithoutAlbum
                                      || ((candidate.AlbumId is not { } albumId
                                           || albums.GetValueOrDefault(albumId) < RecommendationTuning.Diversity.MaxPerAlbum)
                                          && (relaxation >= CapRelaxation.WithoutGenre
                                              || candidate.GenreId is not { } genreId
                                              || genres.GetValueOrDefault(genreId) < RecommendationTuning.Diversity.MaxPerGenre))));

                if (!allowed)
                    continue;

                var artistsTaken = Credits(candidate).Select(id => artists.GetValueOrDefault(id)).Prepend(0).Max();

                var value = (1 - lambda) * candidate.Score
                            - lambda * penalties[index]
                            - repeatPenalty * artistsTaken;

                if (!(value > bestValue)) continue;
                bestValue = value;
                bestIndex = index;
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

            Take(chosen);
            selected.Add(chosen);

            Absorb(pool, penalties, chosen, vectors);
        }

        return selected;

        void Take(RecommendationCandidate candidate)
        {
            foreach (var artistId in Credits(candidate))
                artists[artistId] = artists.GetValueOrDefault(artistId) + 1;

            if (candidate.AlbumId is { } albumId)
                albums[albumId] = albums.GetValueOrDefault(albumId) + 1;

            if (candidate.GenreId is { } genreId)
                genres[genreId] = genres.GetValueOrDefault(genreId) + 1;
        }

        static IEnumerable<Guid> Credits(RecommendationCandidate candidate) =>
            candidate.ArtistIds.Count > 0 ? candidate.ArtistIds : [candidate.ArtistId];
    }

    // Сходство — максимум из метаданных (тот же трек > альбом > артист > жанр > близкий год)
    // и звучания (косинус, растянутый между порогом и насыщением и ограниченный сверху,
    // чтобы похожий звук не перевешивал общего артиста).
    private static void Absorb(
        List<RecommendationCandidate> pool,
        double[] penalties,
        RecommendationCandidate taken,
        EmbeddingSnapshot? vectors)
    {
        for (var index = 0; index < pool.Count; index++)
        {
            var other = pool[index];

            double metadata;
            if (other.TrackId == taken.TrackId)
                metadata = 1.0;
            else if (other.AlbumId is not null && other.AlbumId == taken.AlbumId)
                metadata = 0.9;
            else if (other.ArtistId == taken.ArtistId
                     || other.ArtistIds.Any(id => id == taken.ArtistId || taken.ArtistIds.Contains(id)))
                metadata = 0.8;
            else if (other.GenreId is not null && other.GenreId == taken.GenreId)
                metadata = 0.4;
            else if (other.Year is { } otherYear && taken.Year is { } takenYear)
                metadata = 0.2 * Math.Exp(-Math.Abs(otherYear - takenYear) / 10.0);
            else
                metadata = 0;

            var sonic = vectors is null || other.EmbeddingRow < 0 || taken.EmbeddingRow < 0
                ? 0
                : Math.Clamp(
                      (vectors.Between(other.EmbeddingRow, taken.EmbeddingRow) - SonicFloor) / (SonicSaturation - SonicFloor),
                      0, 1) * SonicCeiling;

            penalties[index] = Math.Max(penalties[index], Math.Max(metadata, sonic));
        }
    }

    private enum CapRelaxation
    {
        None = 0,
        WithoutGenre = 1,
        WithoutAlbum = 2,
        All = 3,
    }
}
