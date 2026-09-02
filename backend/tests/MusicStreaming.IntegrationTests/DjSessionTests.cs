// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class DjSessionTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_dj_session_requires_a_listener()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var response = await fixture.CreateAnonymousClient().PostAsJsonAsync(
            "/api/recommendations/dj",
            Request(DjMode.ForYou, DjVariety.Balanced),
            Cancel.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_starts_for_a_cold_listener_without_repeats()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        var batch = await PostAsync(client, Request(DjMode.Discover, DjVariety.Adventurous, limit: 10));

        Assert.Equal(DjMode.Discover, batch.Mode);
        Assert.Equal(DjVariety.Adventurous, batch.Variety);
        Assert.NotEmpty(batch.Tracks);
        Assert.True(batch.Tracks.Count <= 10);
        Assert.Distinct(batch.Tracks.Select(item => item.Track.Id));
        Assert.All(batch.Tracks, item => Assert.Equal("discovery", item.Reason.Kind));
    }

    [Fact]
    public async Task Explicit_dj_works_when_generic_autoplay_is_off()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        (await client.PutAsJsonAsync("/api/me/settings", new { autoplay = false }, Cancel.Token))
            .EnsureSuccessStatusCode();

        var batch = await PostAsync(client, Request(DjMode.ForYou, DjVariety.Balanced));

        Assert.NotEmpty(batch.Tracks);
    }

    [Fact]
    public async Task Queue_exclusions_are_respected_between_batches()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        var first = await PostAsync(client, Request(DjMode.ForYou, DjVariety.Balanced, limit: 5));
        var excluded = first.Tracks.Select(item => item.Track.Id).ToArray();

        var second = await PostAsync(
            client,
            Request(DjMode.ForYou, DjVariety.Balanced, exclude: excluded, limit: 5));

        Assert.DoesNotContain(second.Tracks, item => excluded.Contains(item.Track.Id));
    }

    [Fact]
    public async Task Generated_tracks_are_recorded_as_dj_impressions()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        var batch = await PostAsync(client, Request(DjMode.ForYou, DjVariety.Balanced, limit: 5));

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var impressions = await db.RecommendationImpressions.AsNoTracking()
            .Where(item => item.UserId == library.UserId && item.ShelfKey == "dj:foryou")
            .ToListAsync(Cancel.Token);

        Assert.Equal(batch.Tracks.Count, impressions.Count);
        var expected = batch.Tracks.Select(item => item.Track.Id).ToHashSet();
        Assert.All(impressions, item => Assert.Contains(item.TrackId, expected));
    }

    [Fact]
    public async Task Rediscovery_prefers_positive_history_older_than_a_month()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        var now = DateTimeOffset.UtcNow;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserTrackAffinities.AddRange(Enumerable.Range(0, 6).Select(index => new UserTrackAffinity
            {
                UserId = library.UserId,
                TrackId = library.Track(index),
                PlayCount = 3,
                CompletedCount = 2,
                CompletionSum = 1.8,
                CompletionSamples = 2,
                DecayedWeight = 1,
                DecayAnchor = now,
                Score = 0.8,
                FirstPlayedAt = now.AddDays(-240),
                LastPlayedAt = index < 4 ? now.AddDays(-90 - index) : now.AddDays(-2),
                UpdatedAt = now,
            }));
            await db.SaveChangesAsync(Cancel.Token);
        }

        var batch = await PostAsync(client, Request(DjMode.Rediscover, DjVariety.Familiar, limit: 4));
        var old = library.TrackIds.Take(4).ToHashSet();

        Assert.Equal(4, batch.Tracks.Count);
        Assert.All(batch.Tracks, item => Assert.Contains(item.Track.Id, old));
        Assert.All(batch.Tracks, item => Assert.Equal("rediscovery", item.Reason.Kind));
    }

    [Fact]
    public async Task Deep_cuts_return_only_unheard_tracks_by_artists_the_listener_plays()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        var now = DateTimeOffset.UtcNow;

        // Сеялка отдаёт артисту a треки [a * 5, a * 5 + 5). Любим только нулевого, из его пяти
        // треков два уже слушали — значит режим обязан вернуть ровно оставшиеся три.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.UserArtistAffinities.Add(new UserArtistAffinity
            {
                UserId = library.UserId,
                ArtistId = library.Artist(0),
                PlayCount = 6,
                DecayedWeight = 1,
                DecayAnchor = now,
                Score = 0.9,
                LastPlayedAt = now.AddDays(-60),
                UpdatedAt = now,
            });

            db.UserTrackAffinities.AddRange(Enumerable.Range(0, 2).Select(index => new UserTrackAffinity
            {
                UserId = library.UserId,
                TrackId = library.Track(index),
                PlayCount = 3,
                CompletedCount = 2,
                CompletionSum = 1.8,
                CompletionSamples = 2,
                DecayedWeight = 1,
                DecayAnchor = now,
                Score = 0.8,
                FirstPlayedAt = now.AddDays(-240),
                LastPlayedAt = now.AddDays(-60),
                UpdatedAt = now,
            }));

            await db.SaveChangesAsync(Cancel.Token);
        }

        var batch = await PostAsync(client, Request(DjMode.DeepCuts, DjVariety.Adventurous, limit: 10));
        var expected = library.TrackIds.Skip(2).Take(3).ToHashSet();

        Assert.NotEmpty(batch.Tracks);
        Assert.All(batch.Tracks, item => Assert.Contains(item.Track.Id, expected));
        Assert.All(batch.Tracks, item => Assert.Equal("deepCut", item.Reason.Kind));
    }

    [Fact]
    public async Task Invalid_limits_are_rejected()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync(artistCount: 6, tracksPerArtist: 5);
        var response = await client.PostAsJsonAsync(
            "/api/recommendations/dj",
            Request(DjMode.ForYou, DjVariety.Balanced, limit: 21),
            Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static DjRequest Request(
        DjMode mode,
        DjVariety variety,
        Guid[]? exclude = null,
        int? limit = null) => new(mode, variety, null, exclude, limit);

    private static async Task<DjBatchDto> PostAsync(HttpClient client, DjRequest request)
    {
        var response = await client.PostAsJsonAsync(
            "/api/recommendations/dj", request, RecommendationApiFixture.Json, Cancel.Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DjBatchDto>(RecommendationApiFixture.Json, Cancel.Token))!;
    }

}
