using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;
using Xunit;

using static MusicStreaming.UnitTests.Recommendations.CandidateBuilder;

namespace MusicStreaming.UnitTests.Recommendations;

public class ExplorerTests
{
    private static List<RecommendationCandidate> Pool(int familiar, int novel) =>
    [
        .. Enumerable.Range(0, familiar).Select(index => Candidate(score: 0.9 - index * 0.001)),
        .. Enumerable.Range(0, novel).Select(index => Candidate(score: 0.5 - index * 0.001, novel: true)),
    ];

    [Fact]
    public void A_quarter_of_the_shelf_explores()
    {
        var shelf = Explorer.Compose(Pool(familiar: 50, novel: 50), 12, 0.25, Options(), seed: 1);

        Assert.Equal(12, shelf.Count);
        Assert.Equal(3, shelf.Count(c => c.IsNovel));
    }

    [Fact]
    public void The_discovery_ratio_inverts_the_balance()
    {
        var shelf = Explorer.Compose(Pool(familiar: 50, novel: 50), 12, 0.60, Options(), seed: 1);

        Assert.True(shelf.Count(c => c.IsNovel) >= 7);
    }

    [Fact]
    public void No_exploration_means_no_novel_picks()
    {
        var shelf = Explorer.Compose(Pool(familiar: 50, novel: 50), 12, 0, Options(), seed: 1);

        Assert.DoesNotContain(shelf, c => c.IsNovel);
    }

    /// <summary>
    /// Слушателю без истории не на что знакомое опереться. Его первая полка всё равно обязана быть
    /// полной: это путь холодного старта, где любой кандидат нов по определению.
    /// </summary>
    [Fact]
    public void A_shelf_with_nothing_familiar_is_still_filled()
    {
        var shelf = Explorer.Compose(Pool(familiar: 0, novel: 40), 12, 0.25, Options(), seed: 1);

        Assert.Equal(12, shelf.Count);
        Assert.All(shelf, candidate => Assert.True(candidate.IsNovel));
    }

    /// <summary>Противоположный угол: тот, кто прослушал всю библиотеку.</summary>
    [Fact]
    public void A_shelf_with_nothing_novel_is_still_filled()
    {
        var shelf = Explorer.Compose(Pool(familiar: 40, novel: 0), 12, 0.25, Options(), seed: 1);

        Assert.Equal(12, shelf.Count);
        Assert.DoesNotContain(shelf, c => c.IsNovel);
    }

    [Fact]
    public void A_pool_smaller_than_the_shelf_is_returned_whole()
    {
        var shelf = Explorer.Compose(Pool(familiar: 3, novel: 2), 12, 0.25, Options(), seed: 1);

        Assert.Equal(5, shelf.Count);
    }

    [Fact]
    public void An_empty_pool_yields_an_empty_shelf() =>
        Assert.Empty(Explorer.Compose([], 12, 0.25, Options(), seed: 1));

    /// <summary>
    /// Обновление страницы не должно перетасовывать музыку под курсором читателя.
    /// </summary>
    [Fact]
    public void The_same_seed_produces_the_same_shelf()
    {
        var pool = Pool(familiar: 50, novel: 50);

        var first = Explorer.Compose(pool, 12, 0.25, Options(), seed: 42);
        var second = Explorer.Compose(pool, 12, 0.25, Options(), seed: 42);

        Assert.Equal(first.Select(c => c.TrackId), second.Select(c => c.TrackId));
    }

    /// <summary>Новые находки рассыпаны по полке, а не свалены в конец, куда никто не долистывает.</summary>
    [Fact]
    public void Exploration_is_not_all_pushed_to_the_end()
    {
        var shelf = Explorer.Compose(Pool(familiar: 50, novel: 50), 12, 0.25, Options(), seed: 7);

        var novelPositions = shelf
            .Select((candidate, index) => (candidate, index))
            .Where(pair => pair.candidate.IsNovel)
            .Select(pair => pair.index)
            .ToList();

        Assert.NotEmpty(novelPositions);
        Assert.True(novelPositions.Min() < shelf.Count - novelPositions.Count);
    }

    [Fact]
    public void Nothing_appears_twice()
    {
        var shelf = Explorer.Compose(Pool(familiar: 50, novel: 50), 12, 0.25, Options(), seed: 3);

        Assert.Equal(shelf.Count, shelf.Select(c => c.TrackId).Distinct().Count());
    }

    [Fact]
    public void The_seed_is_stable_for_a_user_and_a_day()
    {
        var user = Guid.CreateVersion7();
        var morning = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var evening = new DateTimeOffset(2026, 8, 12, 22, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            Explorer.SeedFor(user, "forYou", morning),
            Explorer.SeedFor(user, "forYou", evening));
    }

    [Fact]
    public void The_seed_moves_on_to_the_next_day()
    {
        var user = Guid.CreateVersion7();
        var today = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

        Assert.NotEqual(
            Explorer.SeedFor(user, "forYou", today),
            Explorer.SeedFor(user, "forYou", today.AddDays(1)));
    }

    [Fact]
    public void Different_users_and_shelves_get_different_seeds()
    {
        var now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
        var user = Guid.CreateVersion7();

        Assert.NotEqual(Explorer.SeedFor(user, "forYou", now), Explorer.SeedFor(user, "discover", now));
        Assert.NotEqual(
            Explorer.SeedFor(user, "forYou", now),
            Explorer.SeedFor(Guid.CreateVersion7(), "forYou", now));
    }

    [Fact]
    public void The_seed_is_never_negative()
    {
        for (var index = 0; index < 200; index++)
            Assert.True(Explorer.SeedFor(Guid.CreateVersion7(), "forYou", Now) >= 0);
    }
}
