// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RadioTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_exhausted_queue_is_continued_with_neighbours_of_the_last_track()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));

        Assert.NotEmpty(batch.Tracks);
        Assert.Equal(library.Track(0), batch.SeedTrackId);

        Assert.DoesNotContain(batch.Tracks, item => item.Track.Id == library.Track(0));
        Assert.Equal(
            batch.Tracks.Select(item => item.Track.Id).Distinct().Count(),
            batch.Tracks.Count);
    }

    [Fact]
    public async Task Autoplay_turned_off_means_nothing_is_generated()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        await SetAutoplayAsync(client, false);

        try
        {
            var batch = await NextAsync(client, new RadioRequest(library.Track(0), [], null));
            Assert.Empty(batch.Tracks);
        }
        finally
        {
            await SetAutoplayAsync(client, true);
        }
    }

    [Fact]
    public async Task Tracks_already_in_the_queue_are_never_offered_again()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var first = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));
        Assert.NotEmpty(first.Tracks);

        var queued = first.Tracks.Select(item => item.Track.Id).Append(library.Track(0)).ToList();
        var second = await NextAsync(client, new RadioRequest(library.Track(0), queued, null));

        Assert.DoesNotContain(second.Tracks, item => queued.Contains(item.Track.Id));
    }

    [Fact]
    public async Task A_track_played_in_the_last_day_is_not_offered()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var justPlayed = library.Track(1);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;

            db.UserTrackAffinities.Add(new UserTrackAffinity
            {
                UserId = library.UserId,
                TrackId = justPlayed,
                PlayCount = 1,
                Score = 0.5,
                DecayAnchor = now,
                FirstPlayedAt = now.AddHours(-2),
                LastPlayedAt = now.AddHours(-2),
                UpdatedAt = now,
            });

            await db.SaveChangesAsync(Cancel.Token);
        }

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], 20));

        Assert.DoesNotContain(batch.Tracks, item => item.Track.Id == justPlayed);
    }

    [Fact]
    public async Task An_empty_library_produces_an_empty_batch_rather_than_an_error()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        using (var scope = fixture.CreateScope())
            await LibrarySeeder.ClearAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        var batch = await NextAsync(client, new RadioRequest(null, [], null));

        Assert.Empty(batch.Tracks);
        Assert.Null(batch.SeedTrackId);
    }

    [Fact]
    public async Task A_track_without_computed_neighbours_still_continues()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));

        Assert.NotEmpty(batch.Tracks);
    }

    [Fact]
    public async Task The_batch_size_is_five_by_default()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.RefreshSimilarityAsync();

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));

        Assert.Equal(5, batch.Tracks.Count);
    }

    private static async Task<RadioBatchDto> NextAsync(HttpClient client, RadioRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/recommendations/radio", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RadioBatchDto>())!;
    }

    private static async Task SetAutoplayAsync(HttpClient client, bool autoplay)
    {
        var response = await client.PutAsJsonAsync(
            "/api/me/settings", new UpdateUserSettingsRequest(autoplay, null, null, null));

        response.EnsureSuccessStatusCode();
    }

}
