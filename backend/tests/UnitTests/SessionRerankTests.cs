// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using Domain.Entities.Recommendations;
using App.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;

namespace UnitTests.Recommendations;

public class SessionRerankTests
{
    // Шесть треков: первые три звучат «как сессия» (ось x), последние три — наоборот (ось y).
    private static readonly float[][] Sound =
    [
        Vectors.Unit(0.2f, 1f), Vectors.Unit(0.1f, 1f), Vectors.Unit(0f, 1f),
        Vectors.Unit(1f, 0.1f), Vectors.Unit(1f, 0.2f), Vectors.Unit(1f, 0f),
    ];

    private static readonly Guid[] Ids = [.. Sound.Select(_ => Guid.NewGuid())];

    private static readonly EmbeddingSnapshot Snapshot = new(
        [.. Sound.SelectMany(vector => vector)],
        [.. Ids.Select((id, row) => new TrackVectorMeta(id, Guid.NewGuid(), $"h{row}", $"s{row}", DateTimeOffset.UnixEpoch, 0))],
        2);

    private static readonly IReadOnlyList<CachedRecommendation> Shelf =
        [.. Ids.Select((id, position) => Track(id, 1.0 - position * 0.1))];

    [Fact]
    public void Without_a_session_the_shelf_stays_as_built() =>
        Assert.Same(Shelf, SessionRerank.Apply(Shelf, Snapshot, SessionState.Empty));

    [Fact]
    public void Tracks_that_sound_like_the_session_move_up()
    {
        var session = new SessionState(Vectors.Unit(1f, 0f), new HashSet<Guid>());

        var order = SessionRerank.Apply(Shelf, Snapshot, session).Select(item => item.ItemId).ToList();

        // Похожие на сессию поднимаются над непохожими, но лучшая находка движка (0) не падает в конец:
        // 1.0 + 0.2 близости держит её выше шестого трека с 0.17 + 1.0.
        Assert.Equal([Ids[3], Ids[4], Ids[0], Ids[5], Ids[1], Ids[2]], order);
    }

    [Fact]
    public void A_weak_pull_does_not_overturn_the_engine()
    {
        // Сессия смотрит между осями: все треки ей примерно одинаково близки, и порядок движка решает.
        var session = new SessionState(Vectors.Unit(1f, 1f), new HashSet<Guid>());

        var order = SessionRerank.Apply(Shelf, Snapshot, session).Select(item => item.ItemId).ToList();

        Assert.Equal(Ids[0], order[0]);
    }

    [Fact]
    public void What_already_played_in_the_session_leaves_the_shelf()
    {
        var session = new SessionState(null, new HashSet<Guid> { Ids[0], Ids[4] });

        var order = SessionRerank.Apply(Shelf, Snapshot, session).Select(item => item.ItemId).ToList();

        Assert.Equal([Ids[1], Ids[2], Ids[3], Ids[5]], order);
    }

    [Fact]
    public void Artists_are_left_alone()
    {
        var artist = new CachedRecommendation(Guid.NewGuid(), RecommendedItemKind.Artist, 1, "kind", null, null);
        var session = new SessionState(Vectors.Unit(1f, 0f), new HashSet<Guid> { artist.ItemId });

        Assert.Contains(artist, SessionRerank.Apply([artist, .. Shelf], Snapshot, session));
    }

    [Fact]
    public void The_time_of_day_nudges_a_shelf_even_without_a_session()
    {
        // Подсказка поднимает последний трек на полке над остальными.
        var context = new float[Ids.Length];
        context[5] = 0.9f;

        var order = SessionRerank.Apply(Shelf, Snapshot, SessionState.Empty, context).Select(item => item.ItemId).ToList();

        Assert.Equal(Ids[5], order[0]);
    }

    [Fact]
    public void A_context_of_another_library_is_ignored() =>
        Assert.Same(Shelf, SessionRerank.Apply(Shelf, Snapshot, SessionState.Empty, [1f, 2f]));

    [Theory]
    [InlineData("forYou", true)]
    [InlineData("becauseYouListened:abc", true)]
    [InlineData("discover", false)]
    [InlineData("artistsForYou", false)]
    public void Only_shelves_that_follow_the_taste_follow_the_session(string shelfKey, bool follows) =>
        Assert.Equal(follows, SessionRerank.Follows(shelfKey));

    private static CachedRecommendation Track(Guid id, double score) =>
        new(id, RecommendedItemKind.Track, score, "kind", null, null);
}
