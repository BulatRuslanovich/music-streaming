using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Recommendations;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class SimilarTracksTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Tracks_by_the_same_artist_become_neighbours()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await BuildSimilarityAsync();

        var similar = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=10");

        Assert.NotNull(similar);
        Assert.NotEmpty(similar);

        // Ничто не похоже само на себя, а связь по исполнителю должна главенствовать в начале списка.
        Assert.DoesNotContain(similar, item => item.Track.Id == library.Track(0));
        Assert.Contains(similar, item => item.Track.ArtistId == library.Artist(0));
        Assert.All(similar, item => Assert.Equal(ReasonKind, item.Reason.Kind));
    }

    /// <summary>
    /// Обе записи эндпойнта обязаны отвечать одинаково: одна живёт рядом с прочими подресурсами
    /// трека, другая — со всем персональным.
    /// </summary>
    [Fact]
    public async Task The_track_route_mirrors_the_recommendation_route()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await BuildSimilarityAsync();

        var viaTracks = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/tracks/{library.Track(0)}/similar?limit=5");

        var viaRecommendations = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=5");

        Assert.Equal(
            viaRecommendations!.Select(item => item.Track.Id),
            viaTracks!.Select(item => item.Track.Id));
    }

    /// <summary>
    /// Состояние каждого трека в библиотеке, которую ни разу не слушали, и любого трека, загруженного
    /// после последнего прохода обслуживания. «Ничто на это не похоже» почти никогда не правда,
    /// поэтому эндпойнт откатывается к собственному исполнителю и жанру трека.
    /// </summary>
    [Fact]
    public async Task A_track_with_no_computed_neighbours_still_answers()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        // Прохода обслуживания намеренно не было: таблица похожести пуста.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.TrackSimilarities.CountAsync());
        }

        var similar = await client.GetFromJsonAsync<List<RecommendedTrackDto>>(
            $"/api/recommendations/similar/{library.Track(0)}?limit=10");

        Assert.NotNull(similar);
        Assert.NotEmpty(similar);
        Assert.DoesNotContain(similar, item => item.Track.Id == library.Track(0));
    }

    [Fact]
    public async Task An_unknown_track_is_a_not_found()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var response = await client.GetAsync($"/api/recommendations/similar/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_track_takes_its_neighbours_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await StartAsync();
        await BuildSimilarityAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var doomed = library.Track(0);
        Assert.True(await db.TrackSimilarities.AnyAsync(s => s.SimilarTrackId == doomed));

        await db.Tracks.Where(t => t.Id == doomed).ExecuteDeleteAsync();

        // Каскад идёт в обе стороны, поэтому ни одна полка не наполнится треком, которого уже нет.
        Assert.False(await db.TrackSimilarities.AnyAsync(s => s.TrackId == doomed));
        Assert.False(await db.TrackSimilarities.AnyAsync(s => s.SimilarTrackId == doomed));
    }

    /// <summary>
    /// Похожесть хранится в обе стороны, поэтому поиск — это один заход по индексу, а не проход по
    /// двум колонкам.
    /// </summary>
    [Fact]
    public async Task Similarity_is_stored_symmetrically()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        await StartAsync();
        await BuildSimilarityAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pair = await db.TrackSimilarities.AsNoTracking().FirstAsync();

        var mirrored = await db.TrackSimilarities.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TrackId == pair.SimilarTrackId && s.SimilarTrackId == pair.TrackId);

        Assert.NotNull(mirrored);
        Assert.Equal(pair.Score, mirrored.Score, precision: 10);
    }

    [Fact]
    public async Task Content_similarity_ranks_a_shared_artist_above_a_shared_genre()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await StartAsync();
        await BuildSimilarityAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == library.Track(0))
            .OrderByDescending(s => s.Score)
            .Select(s => new { s.SimilarTrackId, s.Score, ArtistId = s.SimilarTrack!.ArtistId })
            .ToListAsync();

        Assert.NotEmpty(neighbours);

        var bestSameArtist = neighbours.Where(n => n.ArtistId == library.Artist(0)).Max(n => n.Score);
        var bestOtherArtist = neighbours.Where(n => n.ArtistId != library.Artist(0))
            .Select(n => (double?)n.Score).Max() ?? 0;

        Assert.True(
            bestSameArtist > bestOtherArtist,
            $"Same artist scored {bestSameArtist}, a different one {bestOtherArtist}");
    }

    private const string ReasonKind = "similarTo";

    private async Task<(SeededLibrary Library, HttpClient Client)> StartAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var library = await LibrarySeeder.SeedAsync(db);
        return (library, await fixture.CreateSignedInClientAsync());
    }

    private async Task BuildSimilarityAsync()
    {
        using var scope = fixture.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<SimilarityMaintenance>();

        await maintenance.RefreshTrackStatsAsync();
        await maintenance.RefreshSimilarityAsync();
    }
}
