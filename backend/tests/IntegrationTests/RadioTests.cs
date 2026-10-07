// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using App.Dtos;
using Domain.Entities.Recommendations;
using Infrastructure.Persistence;
using Xunit;
using App.Recommendations;

namespace IntegrationTests;

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

    [Fact]
    public async Task What_the_listener_played_next_leads_the_radio()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        // Одинаковый звук у всех треков: порядок решают только переходы и буст новинок.
        // Без переходов первым был бы Track(1) — он новее Track(2).
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var vector = new float[32];
            vector[0] = 1;

            db.TrackEmbeddings.AddRange(library.TrackIds.Select(trackId => new TrackEmbedding
            {
                TrackId = trackId,
                Vector = vector,
                Dimension = vector.Length,
                ModelId = "test",
                Strategy = "test",
                Succeeded = true,
                AnalyzedAt = DateTimeOffset.UtcNow,
            }));

            var startedAt = DateTimeOffset.UtcNow.AddDays(-3);

            foreach (var session in Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()))
            {
                db.PlaybackEvents.Add(Started(library.UserId, session, library.Track(0), startedAt));
                db.PlaybackEvents.Add(Started(library.UserId, session, library.Track(2), startedAt.AddMinutes(3)));
                startedAt = startedAt.AddHours(1);
            }

            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.ReloadEmbeddingIndexAsync();

        var batch = await NextAsync(client, new RadioRequest(library.Track(0), [library.Track(0)], null));

        Assert.Equal(library.Track(2), batch.Tracks[0].Track.Id);
    }

    [Fact]
    public async Task Moods_are_listed_and_start_a_radio()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();
        await fixture.EmbedLibraryAsync();

        var moods = await client.GetFromJsonAsync<List<string>>("/api/recommendations/moods", Cancel.Token);
        Assert.NotNull(moods);
        Assert.Contains("workout", moods);

        var batch = await NextAsync(client, new RadioRequest(null, [], 6, "workout"));

        Assert.NotEmpty(batch.Tracks);
    }

    [Fact]
    public async Task An_unknown_mood_is_rejected()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.PostAsJsonAsync("/api/recommendations/radio", new RadioRequest(null, [], 6, "nonsense"), Cancel.Token);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static PlaybackEvent Started(Guid userId, Guid sessionId, Guid trackId, DateTimeOffset at) => new()
    {
        UserId = userId,
        TrackId = trackId,
        Type = PlaybackEventType.TrackStarted,
        OccurredAt = at,
        DurationSeconds = 180,
        SessionId = sessionId,
    };

    private static async Task<RadioBatchDto> NextAsync(HttpClient client, RadioRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/recommendations/radio", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RadioBatchDto>())!;
    }
}
