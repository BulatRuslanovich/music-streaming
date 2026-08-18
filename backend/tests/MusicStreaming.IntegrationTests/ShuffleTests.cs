using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class ShuffleTests(RecommendationApiFixture fixture)
{
    private const int ArtistCount = 8;
    private const int TracksPerArtist = 15;
    private const int LibrarySize = ArtistCount * TracksPerArtist;

    [Fact]
    public async Task Shuffling_draws_on_the_whole_library()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        var shuffled = await ShuffleAsync(client);

        Assert.Equal(LibrarySize, shuffled.Count);
        Assert.Equal(library.TrackIds.OrderBy(id => id), shuffled.Select(t => t.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Two_shuffles_do_not_come_back_in_the_same_order()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var first = await ShuffleAsync(client);
        var second = await ShuffleAsync(client);

        Assert.NotEqual(first.Select(t => t.Id), second.Select(t => t.Id));
    }

    [Fact]
    public async Task A_short_queue_is_still_sampled_from_everywhere()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var seen = new HashSet<Guid>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var shuffled = await ShuffleAsync(client, limit: 5);

            Assert.Equal(5, shuffled.Count);
            Assert.Equal(5, shuffled.Select(t => t.Id).Distinct().Count());

            seen.UnionWith(shuffled.Select(t => t.Id));
        }

        Assert.True(
            seen.Count > 25,
            $"За десять выборок по пять из {LibrarySize} треков показалось лишь {seen.Count}.");
    }

    [Fact]
    public async Task A_search_narrows_what_gets_shuffled()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var shuffled = await ShuffleAsync(client, search: "Album 3");

        Assert.Equal(TracksPerArtist, shuffled.Count);
        Assert.All(shuffled, track => Assert.Equal("Album 3", track.AlbumTitle));
    }

    [Fact]
    public async Task The_queue_never_grows_past_its_ceiling()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var shuffled = await ShuffleAsync(client, limit: CatalogService.MaxShuffleTracks + 5_000);

        Assert.Equal(LibrarySize, shuffled.Count);
        Assert.True(shuffled.Count <= CatalogService.MaxShuffleTracks);
    }

    [Fact]
    public async Task An_empty_library_shuffles_into_nothing()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        using (var scope = fixture.CreateScope())
            await LibrarySeeder.ClearAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        Assert.Empty(await ShuffleAsync(await fixture.CreateSignedInClientAsync()));
    }

    private static async Task<IReadOnlyList<TrackDto>> ShuffleAsync(
        HttpClient client, int? limit = null, string? search = null)
    {
        var url = "/api/tracks/shuffle";
        var parameters = new List<string>();

        if (limit is not null) parameters.Add($"limit={limit}");
        if (search is not null) parameters.Add($"q={Uri.EscapeDataString(search)}");
        if (parameters.Count > 0) url += "?" + string.Join('&', parameters);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<TrackDto>>())!;
    }

    private async Task<(SeededLibrary Library, HttpClient Client)> StartAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var library = await LibrarySeeder.SeedAsync(db, ArtistCount, TracksPerArtist);
        return (library, await fixture.CreateSignedInClientAsync());
    }
}
