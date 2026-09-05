// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class TagBrowseTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_tag_of_the_artist_reaches_its_tracks_with_a_reduced_weight()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await WriteAsync(db => db.ArtistTags.Add(
            new ArtistTag { ArtistId = library.Artist(0), Name = "shoegaze", Weight = 1.0 }));

        var tags = await client.GetFromJsonAsync<List<TagWeightDto>>(
            $"/api/tracks/{library.Track(0)}/tags", Cancel.Token);

        var tag = Assert.Single(tags!);
        Assert.Equal("shoegaze", tag.Name);
        Assert.Equal(TagWeights.ArtistShare, tag.Weight, 3);

        var page = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            "/api/tags/tracks?name=shoegaze", Cancel.Token);

        Assert.Equal(5, page!.Total);
        Assert.All(page.Items, item => Assert.Equal(library.Artist(0), item.ArtistId));
    }

    [Fact]
    public async Task A_tag_of_the_track_itself_outranks_the_one_it_inherited()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        // Пять треков получают тег от исполнителя (1.0 × доля), шестой носит его сам и слабее
        // единицы, но сильнее унаследованного — он и должен стоять первым.
        var owned = library.Track(10);

        await WriteAsync(db =>
        {
            db.ArtistTags.Add(new ArtistTag
            {
                ArtistId = library.Artist(1),
                Name = "dream pop",
                Weight = 1.0,
            });

            db.TrackTags.Add(new TrackTag { TrackId = owned, Name = "dream pop", Weight = 0.9 });
        });

        var page = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            "/api/tags/tracks?name=dream%20pop", Cancel.Token);

        // Пять треков исполнителя, приглашённое участие в чужом треке и тот, что носит тег сам.
        Assert.Equal(7, page!.Total);
        Assert.Equal(owned, page.Items[0].Id);
        Assert.Contains(page.Items, item => item.Id == library.Track(0));
    }

    [Fact]
    public async Task A_tag_worn_by_too_few_tracks_stays_out_of_the_library_list()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await WriteAsync(db =>
        {
            foreach (var index in new[] { 5, 6, 7 })
                db.TrackTags.Add(new TrackTag { TrackId = library.Track(index), Name = "krautrock", Weight = 0.8 });

            foreach (var index in new[] { 0, 1 })
                db.TrackTags.Add(new TrackTag { TrackId = library.Track(index), Name = "bedroom pop", Weight = 0.8 });
        });

        var tags = await client.GetFromJsonAsync<List<TagDto>>("/api/tags", Cancel.Token);

        var krautrock = Assert.Single(tags!, tag => tag.Name == "krautrock");
        Assert.Equal(3, krautrock.TrackCount);
        Assert.DoesNotContain(tags!, tag => tag.Name == "bedroom pop");
    }

    [Fact]
    public async Task A_tag_card_carries_the_covers_of_the_albums_that_wear_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await WriteAsync(async db =>
        {
            await db.Albums
                .Where(album => album.Id == library.AlbumIds[0])
                .ExecuteUpdateAsync(album => album.SetProperty(a => a.CoverPath, "covers/tag-test.jpg"));

            foreach (var index in new[] { 0, 1, 2 })
                db.TrackTags.Add(new TrackTag { TrackId = library.Track(index), Name = "post-rock", Weight = 0.7 });
        });

        var tags = await client.GetFromJsonAsync<List<TagDto>>("/api/tags", Cancel.Token);
        var postRock = Assert.Single(tags!, tag => tag.Name == "post-rock");

        Assert.Equal([library.AlbumIds[0]], postRock.CoverAlbumIds);
    }

    [Fact]
    public async Task An_unknown_tag_answers_with_an_empty_page_rather_than_a_missing_one()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var page = await client.GetFromJsonAsync<PagedResult<TrackDto>>(
            "/api/tags/tracks?name=nothing%20wears%20this", Cancel.Token);

        Assert.Equal(0, page!.Total);
        Assert.Empty(page.Items);

        var artists = await client.GetFromJsonAsync<List<ArtistDto>>(
            "/api/tags/artists?name=nothing%20wears%20this", Cancel.Token);

        Assert.Empty(artists!);
    }

    [Fact]
    public async Task An_artist_carries_its_own_tags_strongest_first()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await WriteAsync(db => db.ArtistTags.AddRange(
            new ArtistTag { ArtistId = library.Artist(0), Name = "post-punk", Weight = 0.4 },
            new ArtistTag { ArtistId = library.Artist(0), Name = "new wave", Weight = 0.9 }));

        var artist = await client.GetFromJsonAsync<ArtistDetailDto>(
            $"/api/artists/{library.Artist(0)}", Cancel.Token);

        Assert.Equal(["new wave", "post-punk"], artist!.Tags.Select(tag => tag.Name));

        var tagged = await client.GetFromJsonAsync<List<ArtistDto>>(
            "/api/tags/artists?name=new%20wave", Cancel.Token);

        Assert.Equal(library.Artist(0), Assert.Single(tagged!).Id);
    }

    private Task WriteAsync(Action<ApplicationDbContext> write) =>
        WriteAsync(db =>
        {
            write(db);
            return Task.CompletedTask;
        });

    private async Task WriteAsync(Func<ApplicationDbContext, Task> write)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await write(db);
        await db.SaveChangesAsync(Cancel.Token);
    }
}
