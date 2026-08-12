using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Recommendations;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// The read surface and its guarantees: what the endpoints return, what the SQL behind them does,
/// and how fast it answers.
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class RecommendationApiTests(RecommendationApiFixture fixture)
{
    /// <summary>Reads must stay far inside the budget, since the work happens in the background.</summary>
    private const int LatencyBudgetMs = 200;

    [Fact]
    public async Task Every_endpoint_requires_a_session()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var anonymous = fixture.CreateClient();

        foreach (var path in new[]
                 {
                     "/api/recommendations/home",
                     "/api/recommendations/tracks",
                     "/api/recommendations/artists",
                     "/api/recommendations/albums",
                     $"/api/recommendations/similar/{Guid.CreateVersion7()}",
                     "/api/admin/recommendations/stats",
                 })
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task An_event_batch_is_accepted_even_when_parts_of_it_are_unusable()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/events", new
        {
            events = new object[]
            {
                new { type = "trackCompleted", trackId = library.Track(0), listenedSeconds = 200, durationSeconds = 200, sessionId = Guid.CreateVersion7() },
                new { type = "somethingFromTheFuture", trackId = library.Track(1) },
                new { type = "trackCompleted", trackId = (Guid?)null },
                new { type = "artistOpened", entityId = library.Artist(0) },
            },
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RecordEventsResultDto>();

        Assert.NotNull(result);
        Assert.Equal(2, result.Accepted);
        Assert.Equal(2, result.Rejected);
    }

    [Fact]
    public async Task An_empty_batch_is_harmless()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/events", new { events = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task The_track_feed_is_paged()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync(artistCount: 10, tracksPerArtist: 4);
        await BuildEverythingAsync(library.UserId);

        var first = await client.GetFromJsonAsync<PagedResult<RecommendedTrackDto>>(
            "/api/recommendations/tracks?page=1&pageSize=5");

        Assert.NotNull(first);
        Assert.Equal(1, first.Page);
        Assert.Equal(5, first.PageSize);
        Assert.True(first.Items.Count <= 5);

        if (first.Total <= 5)
            return;

        var second = await client.GetFromJsonAsync<PagedResult<RecommendedTrackDto>>(
            "/api/recommendations/tracks?page=2&pageSize=5");

        // Pages must not overlap: the feed is one ranked list, not a fresh draw per request.
        Assert.Empty(first.Items.Select(i => i.Track.Id).Intersect(second!.Items.Select(i => i.Track.Id)));
    }

    /// <summary>
    /// Shelves cache ids, never rendered rows, precisely so that a track deleted after generation
    /// simply disappears instead of surfacing as a broken card.
    /// </summary>
    [Fact]
    public async Task A_track_deleted_after_generation_vanishes_from_its_shelf()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await BuildEverythingAsync(library.UserId);

        var before = await client.GetFromJsonAsync<RecommendationHomeDto>("/api/recommendations/home");
        var doomed = before!.Sections.First(s => s.Tracks is { Count: > 0 }).Tracks![0].Track.Id;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Tracks.Where(t => t.Id == doomed).ExecuteDeleteAsync();
        }

        // No regeneration in between: the shelf still lists the deleted id. It disappears because
        // hydration runs on every read and simply finds nothing to render it with.
        var after = await client.GetFromJsonAsync<RecommendationHomeDto>("/api/recommendations/home");

        var stillThere = after!.Sections
            .Where(s => s.Tracks is not null)
            .SelectMany(s => s.Tracks!)
            .Any(item => item.Track.Id == doomed);

        Assert.False(stillThere, "A deleted track was still served from the shelf cache");
    }

    [Fact]
    public async Task Administrators_can_see_what_the_engine_is_doing()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await BuildEverythingAsync(library.UserId);

        var stats = await client.GetFromJsonAsync<RecommendationStatsDto>(
            "/api/admin/recommendations/stats");

        Assert.NotNull(stats);
        Assert.True(stats.SimilarityRows > 0);
        Assert.True(stats.CachedShelves > 0);
        Assert.NotEmpty(stats.ShelfSizes);
    }

    /// <summary>
    /// The rollup pages through one user's new events on every pass, so this query has to be an
    /// index scan. Left to a sequential scan it degrades with the whole event log, which is the
    /// one table guaranteed to grow without bound.
    /// </summary>
    [Fact]
    public async Task The_rollup_query_uses_its_index()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await StartAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var plan = await ExplainAsync(db, $"""
            SELECT * FROM playback_events
            WHERE user_id = '{library.UserId}' AND sequence > 0
            ORDER BY sequence
            LIMIT 2000
            """);

        Assert.Contains("ix_playback_events_user_id_sequence", plan);
    }

    [Fact]
    public async Task The_shelf_read_uses_its_index()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, _) = await StartAsync();
        await BuildEverythingAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var plan = await ExplainAsync(db, $"""
            SELECT * FROM recommendation_cache
            WHERE user_id = '{library.UserId}'
            ORDER BY position
            """);

        Assert.DoesNotContain("Seq Scan on recommendation_cache", plan);
    }

    /// <summary>
    /// The whole architecture — background generation, cached shelves, hydration by id — exists to
    /// keep this number small. If it regresses, something expensive has moved onto the read path.
    /// </summary>
    [Fact]
    public async Task A_cached_home_page_is_served_well_inside_its_budget()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync(artistCount: 30, tracksPerArtist: 10);
        await BuildEverythingAsync(library.UserId);

        // Warm the connection pool and the shelf cache first; the first request of a process is
        // not what a listener experiences.
        await client.GetAsync("/api/recommendations/home?sectionSize=12");

        var timings = new List<double>();

        for (var index = 0; index < 30; index++)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var response = await client.GetAsync("/api/recommendations/home?sectionSize=12");
            timings.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            response.EnsureSuccessStatusCode();
        }

        timings.Sort();
        var p95 = timings[(int)(timings.Count * 0.95)];

        Assert.True(p95 < LatencyBudgetMs, $"p95 was {p95:0.0} ms over {timings.Count} requests");
    }

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

    private async Task<(SeededLibrary Library, HttpClient Client)> StartAsync(
        int artistCount = 4, int tracksPerArtist = 5)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var library = await LibrarySeeder.SeedAsync(db, artistCount, tracksPerArtist);
        return (library, await fixture.CreateSignedInClientAsync());
    }

    private async Task BuildEverythingAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        var provider = scope.ServiceProvider;

        await provider.GetRequiredService<ProfileRollupService>().RollupAsync(userId);

        var maintenance = provider.GetRequiredService<SimilarityMaintenance>();
        await maintenance.RefreshTrackStatsAsync();
        await maintenance.RefreshSimilarityAsync();

        await provider.GetRequiredService<ShelfGenerationService>()
            .GenerateAsync(userId, Guid.CreateVersion7());
    }
}
