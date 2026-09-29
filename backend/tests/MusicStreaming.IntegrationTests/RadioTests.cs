// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;
using MusicStreaming.Application.Recommendations;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RadioTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_exhausted_queue_is_continued_from_the_last_track()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.EmbedLibraryAsync();

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));

        Assert.NotEmpty(batch.Tracks);
        Assert.Equal(library.Track(0), batch.SeedTrackId);

        Assert.DoesNotContain(batch.Tracks, item => item.Track.Id == library.Track(0));
        Assert.Equal(
            batch.Tracks.Select(item => item.Track.Id).Distinct().Count(),
            batch.Tracks.Count);
    }

    [Fact]
    public async Task Tracks_already_in_the_queue_are_never_offered_again()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.EmbedLibraryAsync();

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
        await fixture.EmbedLibraryAsync();

        var justPlayed = library.Track(1);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.PlaybackEvents.Add(new PlaybackEvent
            {
                UserId = library.UserId,
                TrackId = justPlayed,
                Type = PlaybackEventType.TrackCompleted,
                OccurredAt = fixture.Clock.GetUtcNow().AddHours(-2),
                ListenedSeconds = 180,
                DurationSeconds = 180,
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

        await fixture.ReloadEmbeddingIndexAsync();

        var batch = await NextAsync(client, new RadioRequest(null, [], null));

        Assert.Empty(batch.Tracks);
        Assert.Null(batch.SeedTrackId);
    }

    [Fact]
    public async Task The_batch_size_comes_from_the_queue_setting()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.EmbedLibraryAsync();

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));

        Assert.Equal(RecommendationTuning.Exploration.QueueSize, batch.Tracks.Count);
    }

    private static async Task<RadioBatchDto> NextAsync(HttpClient client, RadioRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/recommendations/radio", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RadioBatchDto>())!;
    }
}
