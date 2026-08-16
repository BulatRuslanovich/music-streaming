using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Порядок выдачи поиска. Ранжирует его функция базы, поэтому проверять его в отрыве от PostgreSQL
/// бессмысленно: провайдер в памяти не умеет ни <c>starts_with</c>, ни <c>position</c>, на которых
/// оно построено.
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class SearchRelevanceTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task Titles_are_ordered_from_exact_match_outwards()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await SeedTitlesAsync(
            "All You Need Is Love",   // слово начинается с запроса
            "Love Actually",          // начинается с запроса
            "Glove Compartment",      // просто содержит
            "Love",                   // точное совпадение
            "Love Songs");            // начинается с запроса

        var results = await SearchAsync(client, "love");

        var titles = results.Tracks.Select(t => t.Title).ToList();

        Assert.Equal("Love", titles[0]);
        Assert.Equal(["Love Actually", "Love Songs"], titles.Skip(1).Take(2));
        Assert.Equal("All You Need Is Love", titles[3]);
        Assert.Equal("Glove Compartment", titles[4]);
    }

    [Fact]
    public async Task Matching_is_case_and_spacing_insensitive()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await SeedTitlesAsync("Love");

        foreach (var query in (string[])["LOVE", "  love  ", "LoVe"])
            Assert.Equal("Love", (await SearchAsync(client, query)).Tracks[0].Title);
    }

    [Fact]
    public async Task Wildcards_typed_by_a_person_are_searched_for_literally()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await SeedTitlesAsync("50% Off", "Fifty Percent", "a_b", "axb");

        var percent = await SearchAsync(client, "50%");
        Assert.Single(percent.Tracks);
        Assert.Equal("50% Off", percent.Tracks[0].Title);

        // Подчёркивание в LIKE — это «любой символ»; без экранирования нашёлся бы и axb.
        var underscore = await SearchAsync(client, "a_b");
        Assert.Single(underscore.Tracks);
        Assert.Equal("a_b", underscore.Tracks[0].Title);
    }

    [Fact]
    public async Task Within_one_rank_the_more_played_track_comes_first()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await SeedTitlesAsync("Love Actually", "Love Songs");

        // Обе строки начинаются с запроса, поэтому решает популярность, а не алфавит.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var songs = db.Tracks.Single(t => t.Title == "Love Songs");

            db.TrackStats.Add(new TrackStats
            {
                TrackId = songs.Id,
                PlayCount = 500,
                PopularityScore = 0.9,
                ComputedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(Cancel.Token);
        }

        var results = await SearchAsync(client, "love");
        Assert.Equal("Love Songs", results.Tracks[0].Title);
    }

    [Fact]
    public async Task A_track_found_by_its_artist_ranks_below_every_title_match()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await SeedTitlesAsync("Glove Compartment");

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var artist = new Artist { Name = "Love", NormalizedName = Normalize.Key("Love") };
            db.Artists.Add(artist);
            await db.SaveChangesAsync(Cancel.Token);

            var track = NewTrack("Something Else", artist.Id, 99);
            db.Tracks.Add(track);
            await db.SaveChangesAsync(Cancel.Token);

            db.TrackArtists.Add(new TrackArtist { TrackId = track.Id, ArtistId = artist.Id });
            await db.SaveChangesAsync(Cancel.Token);
        }

        var results = await SearchAsync(client, "love");

        // «Glove Compartment» совпало названием, «Something Else» — только исполнителем.
        Assert.Equal(["Glove Compartment", "Something Else"], results.Tracks.Select(t => t.Title));

        // А сам исполнитель с точным совпадением — на своём месте, в списке исполнителей.
        Assert.Equal("Love", results.Artists[0].Name);
    }

    [Fact]
    public async Task An_empty_query_returns_nothing_rather_than_everything()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await SeedTitlesAsync("Love");
        var results = await SearchAsync(client, "   ");

        Assert.Empty(results.Tracks);
        Assert.Empty(results.Artists);
    }

    private static async Task<SearchResultDto> SearchAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<SearchResultDto>(
            $"/api/search?q={Uri.EscapeDataString(query)}&limit=50"))!;

    /// <summary>Библиотека ровно из названных треков, каждый со своим исполнителем.</summary>
    private async Task<HttpClient> SeedTitlesAsync(params string[] titles)
    {
        var client = await fixture.CreateSignedInClientAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await LibrarySeeder.ClearAsync(db);

        var tracks = new List<Track>();

        for (var index = 0; index < titles.Length; index++)
        {
            var artist = new Artist
            {
                Name = $"Performer {index}",
                NormalizedName = Normalize.Key($"Performer {index}"),
            };

            db.Artists.Add(artist);
            tracks.Add(NewTrack(titles[index], artist.Id, index));
        }

        await db.SaveChangesAsync();

        db.Tracks.AddRange(tracks);
        await db.SaveChangesAsync();

        db.TrackArtists.AddRange(tracks.Select(t => new TrackArtist { TrackId = t.Id, ArtistId = t.ArtistId }));
        await db.SaveChangesAsync();

        return client;
    }

    private static Track NewTrack(string title, Guid artistId, int index) => new()
    {
        Title = title,
        NormalizedTitle = Normalize.Key(title),
        ArtistId = artistId,
        DurationSeconds = 200,
        FilePath = $"music/search-{index}.mp3",
        OriginalFileName = $"search-{index}.mp3",
        ContentHash = $"search-hash-{index:D8}",
        FileSize = 1_000,
    };
}
