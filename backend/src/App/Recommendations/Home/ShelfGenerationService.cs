// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;

namespace App.Recommendations.Home;

public class ShelfGenerationService(
    ApplicationDbContext db,
    CandidatePool candidatePool,
    EmbeddingIndex embeddingIndex,
    PersonalWeights personalWeights,
    TimeProvider clock)
{
    private const int MinimumShelfSize = 4;

    // На полке «Потому что вы слушали X»: сколько треков самого X и сколько похожих кандидатов на слот.
    private const int OwnTracksOnArtistShelf = 2;
    private const int SimilarPoolPerSlot = 3;

    private record Shelf(string Key, int Position, IReadOnlyList<CachedRecommendation> Items);

    public async Task GenerateAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var (context, candidates) = await candidatePool.LoadAsync(userId, now, ct);

        var weights = (await personalWeights.LoadAsync(userId, ct)).Apply(RankingWeights.Hand(context.Maturity));

        foreach (var candidate in candidates)
            CandidateScorer.Score(candidate, context.Ranking, context.Maturity, weights);

        var shelves = new List<Shelf>();
        var position = 0;

        var vectors = embeddingIndex.Snapshot();

        var used = new HashSet<Guid>();

        void Add(string key, IReadOnlyList<RecommendationCandidate> picks)
        {
            if (picks.Count < MinimumShelfSize)
                return;

            shelves.Add(new Shelf(key, position++, picks.Select(ToCached).ToList()));

            foreach (var pick in picks)
                used.Add(pick.TrackId);
        }

        List<RecommendationCandidate> Pick(
            IEnumerable<RecommendationCandidate> pool, string shelfKey, double explorationRatio, int? maxFamiliar = null)
        {
            var all = pool.ToList();
            var available = all.Where(c => !used.Contains(c.TrackId)).ToList();
            var seed = Explorer.SeedFor(context.UserId, shelfKey, context.Ranking.Now);

            var picks = Explorer.Compose(
                available,
                RecommendationTuning.Shelves.ShelfSize,
                explorationRatio,
                seed,
                vectors,
                maxFamiliar);

            if (picks.Count < MinimumShelfSize)
            {
                picks = Explorer.Compose(
                    all,
                    RecommendationTuning.Shelves.ShelfSize,
                    explorationRatio,
                    seed,
                    vectors,
                    maxFamiliar);
            }

            return picks;
        }

        // «Для вас» — в первую очередь новое для слушателя: знакомое и так звучит в его ротации.
        Add(ShelfKeys.ForYou, Pick(candidates, ShelfKeys.ForYou, RecommendationTuning.Exploration.ShelfRatio,
            (int)Math.Ceiling(RecommendationTuning.Shelves.ShelfSize * RecommendationTuning.Shelves.ForYouFamiliarShare)));

        // «Потому что вы слушали X» — не ещё X, а другие артисты, звучащие как он: ближайшие к центру его
        // звучания кандидаты. Самого X — только пара ещё не слышанных треков. X сменяется по дням; если
        // сегодняшнему не набралось похожих, полку получает следующий.
        foreach (var artistId in ShelfKeys.BecauseYouListenedArtists(context))
        {
            var key = $"{ShelfKeys.BecauseYouListened}:{artistId}";
            var picks = Pick(SimilarToArtist(candidates, vectors, artistId), key, 0);

            if (picks.Count < MinimumShelfSize)
                continue;

            Add(key, Explain(
                picks, ReasonKinds.BecauseYouListened, context.TopArtistNames.GetValueOrDefault(artistId), artistId));
            break;
        }

        var novel = candidates.Where(c => c.IsNovel).ToList();

        Add(ShelfKeys.Discover, Explain(
            Pick(novel, ShelfKeys.Discover, RecommendationTuning.Exploration.ShelfDiscoveryRatio), ReasonKinds.Discovery));

        // Артисты, которых пользователь ещё не слушает постоянно, — по лучшему треку в кандидатах.
        var grouped = new Dictionary<Guid, (double Score, string Reason, string? Subject, Guid? SubjectId)>();

        foreach (var candidate in candidates)
        {
            var id = candidate.ArtistId;
            if (id == Guid.Empty)
                continue;

            if (!grouped.TryGetValue(id, out var existing) || candidate.Score > existing.Score)
            {
                grouped[id] = (
                    candidate.Score,
                    candidate.ReasonKind,
                    candidate.ReasonSubject,
                    candidate.ReasonSubjectId);
            }
        }

        var establishedArtists = context.TopArtistIds.ToHashSet();

        var artists = grouped
            .Where(pair => !establishedArtists.Contains(pair.Key))
            .OrderByDescending(pair => pair.Value.Score)
            .Take(RecommendationTuning.Shelves.ShelfSize)
            .Select(pair => new CachedRecommendation(
                pair.Key, RecommendedItemKind.Artist, pair.Value.Score,
                pair.Value.Reason, pair.Value.Subject, pair.Value.SubjectId))
            .ToList();

        if (artists.Count >= MinimumShelfSize)
            shelves.Add(new Shelf(ShelfKeys.ArtistsForYou, position++, artists));

        var mixPool = Explorer.Compose(
            candidates,
            RecommendationTuning.Shelves.MixPoolSize,
            RecommendationTuning.Exploration.ShelfRatio,
            Explorer.SeedFor(context.UserId, ShelfKeys.MixPool, context.Ranking.Now),
            vectors);

        if (mixPool.Count > 0)
            shelves.Add(new Shelf(ShelfKeys.MixPool, position, [.. mixPool.Select(ToCached)]));

        var expiresAt = now.AddHours(RecommendationTuning.Shelves.CacheTtlHours);

        var byKey = await db.RecommendationCache
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.ShelfKey, ct);

        foreach (var shelf in shelves)
        {
            if (byKey.Remove(shelf.Key, out var entry))
            {
                entry.Payload = shelf.Items;
                entry.Position = shelf.Position;
                entry.ExpiresAt = expiresAt;
            }
            else
            {
                db.RecommendationCache.Add(new RecommendationCacheEntry
                {
                    UserId = userId,
                    ShelfKey = shelf.Key,
                    Position = shelf.Position,
                    Payload = shelf.Items,
                    ExpiresAt = expiresAt,
                });
            }
        }

        db.RecommendationCache.RemoveRange(byKey.Values);
        await db.SaveChangesAsync(ct);
    }

    private static List<RecommendationCandidate> SimilarToArtist(
        IReadOnlyList<RecommendationCandidate> candidates, EmbeddingSnapshot vectors, Guid artistId)
    {
        var own = candidates
            .Where(c => c.ArtistId == artistId || c.ArtistIds.Contains(artistId))
            .ToList();

        var unheardOwn = own
            .Where(c => !c.IsFamiliar)
            .OrderByDescending(c => c.Score)
            .Take(OwnTracksOnArtistShelf);

        var others = candidates.Except(own);

        // Без эмбеддингов похожесть видна только по жанру: самый частый жанр артиста среди кандидатов.
        if (vectors.CentreOf(artistId) is not { } centre)
        {
            var genre = own
                .Where(c => c.GenreId is not null)
                .GroupBy(c => c.GenreId)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault();

            return [.. unheardOwn, .. others.Where(c => genre is not null && c.GenreId == genre)];
        }

        var similarities = vectors.SimilaritiesTo(centre);

        return
        [
            .. unheardOwn,
            .. others
                .Where(c => c.EmbeddingRow >= 0)
                .OrderByDescending(c => similarities[c.EmbeddingRow])
                .Take(RecommendationTuning.Shelves.ShelfSize * SimilarPoolPerSlot),
        ];
    }

    private static List<RecommendationCandidate> Explain(
        List<RecommendationCandidate> picks, string reasonKind, string? subject = null, Guid? subjectId = null)
    {
        foreach (var pick in picks)
        {
            pick.ReasonKind = reasonKind;
            pick.ReasonSubject = subject;
            pick.ReasonSubjectId = subjectId;
        }

        return picks;
    }

    private static CachedRecommendation ToCached(RecommendationCandidate candidate) => new(
        candidate.TrackId,
        RecommendedItemKind.Track,
        candidate.Score,
        candidate.ReasonKind,
        candidate.ReasonSubject,
        candidate.ReasonSubjectId,
        RankingFeatures.Of(candidate));
}
