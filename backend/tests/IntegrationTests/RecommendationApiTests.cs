// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Domain.Entities.Recommendations;
using Infrastructure.Persistence;
using Xunit;
using App.Recommendations;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RecommendationApiTests(RecommendationApiFixture fixture)
{
    private const int LatencyBudgetMs = 200;

    [Fact]
    public async Task The_radio_requires_a_session()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var anonymous = fixture.CreateClient();

        var response = await anonymous.PostAsync(
            "/api/recommendations/radio",
            JsonContent.Create(new { seedTrackId = Guid.CreateVersion7() }),
            Cancel.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_event_batch_is_accepted_even_when_parts_of_it_are_unusable()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var response = await client.PostAsJsonAsync("/api/playback/signals", new
        {
            events = new object[]
            {
                new { type = "trackCompleted", trackId = library.Track(0), listenedSeconds = 200, durationSeconds = 200, sessionId = Guid.CreateVersion7() },
                new { type = "somethingFromTheFuture", trackId = library.Track(1) },
                new { type = "trackCompleted", trackId = (Guid?)null },
                new { type = "artistOpened", entityId = library.Artist(0) },
            },
        }, Cancel.Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RecordEventsResultDto>(Cancel.Token);

        Assert.NotNull(result);
        Assert.Equal(2, result.Accepted);
        Assert.Equal(2, result.Rejected);
    }

    [Fact]
    public async Task Fields_an_older_client_still_sends_do_not_reject_the_event_batch()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        var response = await client.PostAsJsonAsync("/api/playback/signals", new
        {
            events = new[]
            {
                new
                {
                    type = "trackStarted",
                    trackId = library.Track(0),
                    source = "home",
                    sourceId = "dailyMix",
                    platform = "web",
                    sessionId = Guid.CreateVersion7(),
                },
            },
        }, Cancel.Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_batch_is_harmless()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await fixture.SeedAndSignInAsync();

        var response = await client.PostAsJsonAsync(
            "/api/playback/signals", new { events = Array.Empty<object>() }, Cancel.Token);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_track_deleted_after_generation_vanishes_from_its_shelf()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var before = await fixture.HomeAsync(library.UserId);
        var doomed = before.First(s => s.Tracks is { Count: > 0 }).Tracks![0].Track.Id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Tracks.Where(t => t.Id == doomed).ExecuteDeleteAsync(Cancel.Token);
        }

        var after = await fixture.HomeAsync(library.UserId);

        var stillThere = after
            .Where(s => s.Tracks is not null)
            .SelectMany(s => s.Tracks!)
            .Any(item => item.Track.Id == doomed);

        Assert.False(stillThere, "A deleted track was still served from the shelf cache");
    }

    [Fact]
    public async Task The_rollup_query_uses_its_index()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await FillEventsAsync(db, library, count: 4000);

        var plan = await ExplainAsync(db, $"""
            SELECT * FROM playback_events
            WHERE user_id = '{library.UserId}' AND sequence > 0
            ORDER BY sequence
            LIMIT {ProfileRollupService.BatchSize}
            """);

        Assert.Contains("ix_playback_events_user_id_sequence", plan);
    }

    private static async Task FillEventsAsync(ApplicationDbContext db, SeededLibrary library, int count)
    {
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-30);
        var session = Guid.CreateVersion7();

        for (var index = 0; index < count; index++)
        {
            db.PlaybackEvents.Add(new PlaybackEvent
            {
                UserId = library.UserId,
                TrackId = library.Track(index % library.TrackIds.Count),
                Type = PlaybackEventType.TrackCompleted,
                OccurredAt = occurredAt.AddMinutes(index),
                ListenedSeconds = 200,
                DurationSeconds = 200,
                SessionId = session,
            });
        }

        await db.SaveChangesAsync(Cancel.Token);
        await db.Database.ExecuteSqlRawAsync("ANALYZE playback_events", Cancel.Token);
    }

    [Fact]
    public async Task The_shelf_read_uses_its_index()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            await FillNeighbourShelvesAsync(db);

            await db.Database.ExecuteSqlRawAsync("ANALYZE recommendation_cache", Cancel.Token);

            var plan = await ExplainAsync(db, $"""
                SELECT * FROM recommendation_cache
                WHERE user_id = '{library.UserId}'
                ORDER BY position
                """);

            Assert.DoesNotContain("Seq Scan on recommendation_cache", plan);
        }
        finally
        {
            await RemoveNeighbourShelvesAsync(db);
        }
    }

    [Fact]
    public async Task A_cached_home_page_is_served_well_inside_its_budget()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await fixture.SeedAndSignInAsync(artistCount: 30, tracksPerArtist: 10);
        await fixture.BuildRecommendationsAsync(library.UserId);

        await fixture.HomeAsync(library.UserId);

        var timings = new List<double>();

        for (var index = 0; index < 30; index++)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var home = await fixture.HomeAsync(library.UserId);
            timings.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            Assert.NotEmpty(home);
        }

        timings.Sort();
        var p95 = timings[(int)(timings.Count * 0.95)];

        Assert.True(p95 < LatencyBudgetMs, $"p95 was {p95:0.0} ms over {timings.Count} requests");
    }

    private const string ShelfFiller = "shelffiller-";

    private static async Task FillNeighbourShelvesAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO users (id, username, password_hash, is_admin, is_active, created_at)
            SELECT gen_random_uuid(), '{ShelfFiller}' || g, 'x', false, true, now()
            FROM generate_series(1, 1000) AS g
            ON CONFLICT (username) DO NOTHING
            """,
            Cancel.Token);

        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO recommendation_cache (user_id, shelf_key, position, payload, expires_at)
            SELECT u.id, 'filler-' || p, p, '[]'::jsonb, now() + interval '1 day'
            FROM users u CROSS JOIN generate_series(0, 7) AS p
            WHERE u.username LIKE '{ShelfFiller}%'
            ON CONFLICT (user_id, shelf_key) DO NOTHING
            """,
            Cancel.Token);
    }

    private static Task RemoveNeighbourShelvesAsync(ApplicationDbContext db) =>
        db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM users WHERE username LIKE '{ShelfFiller}%'", Cancel.Token);

    private static async Task<string> ExplainAsync(ApplicationDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();

        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync();

        command.CommandText = $"EXPLAIN {sql}";

        await using var reader = await command.ExecuteReaderAsync();

        var lines = new List<string>();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));

        return string.Join('\n', lines);
    }
}
