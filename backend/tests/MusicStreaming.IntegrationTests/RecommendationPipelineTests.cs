// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services.Recommendations;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RecommendationPipelineTests(RecommendationApiFixture fixture)
{
    private static readonly TimeSpan IngestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Reported_events_are_stored_and_shape_the_profile()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Liked(library.Track(0)),
            Completed(library.Track(1)),
            Skipped(library.Track(10), listened: 3));

        await WaitForEventsAsync(4);
        await RollupAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstAsync(p => p.UserId == library.UserId, Cancel.Token);

        Assert.Equal(3, profile.PositiveSignalCount);
        Assert.Equal(4, profile.TotalEventCount);
        Assert.Equal(3, profile.DistinctTracks);
        Assert.True(profile.SkipRate > 0);

        var loved = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.UserId == library.UserId && a.TrackId == library.Track(0), Cancel.Token);

        var rejected = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.UserId == library.UserId && a.TrackId == library.Track(10), Cancel.Token);

        Assert.True(loved.Score > 0.4, $"A completed and liked track scored {loved.Score}");
        Assert.True(rejected.Score < 0, $"An abandoned track scored {rejected.Score}");
        Assert.Equal(1, rejected.SkipCount);
    }

    [Fact]
    public async Task Rolling_up_twice_does_not_double_count()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client, Completed(library.Track(0)), Completed(library.Track(1)));
        await WaitForEventsAsync(2);

        await RollupAsync(library.UserId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.TrackId == library.Track(0), Cancel.Token);

        await RollupAsync(library.UserId);

        var second = await db.UserTrackAffinities.AsNoTracking()
            .FirstAsync(a => a.TrackId == library.Track(0), Cancel.Token);

        Assert.Equal(first.PlayCount, second.PlayCount);
        Assert.Equal(first.DecayedWeight, second.DecayedWeight, precision: 10);
        Assert.Equal(first.CompletionSamples, second.CompletionSamples);
    }

    [Fact]
    public async Task A_listening_history_produces_personalised_shelves()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Completed(library.Track(1)),
            Completed(library.Track(2)),
            Liked(library.Track(1)),
            Skipped(library.Track(15), listened: 2),
            Skipped(library.Track(16), listened: 3));

        await WaitForEventsAsync(6);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(home);
        Assert.False(home.IsColdStart);
        Assert.NotEmpty(home.Sections);

        Assert.All(home.Sections, section =>
        {
            var count = (section.Tracks?.Count ?? 0) + (section.Artists?.Count ?? 0) + (section.Albums?.Count ?? 0);
            Assert.True(count > 0, $"Shelf {section.Key} came back empty");
            Assert.False(string.IsNullOrWhiteSpace(section.BaseKey));
        });

        var recommended = home.Sections
            .Where(s => s.Tracks is not null)
            .SelectMany(s => s.Tracks!)
            .ToList();

        Assert.NotEmpty(recommended);
        Assert.All(recommended, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason.Kind)));

        Assert.All(recommended, item => Assert.Null(item.Score));

        var forYou = home.Sections.FirstOrDefault(s => s.BaseKey == ShelfKeys.ForYou);
        Assert.NotNull(forYou);
        Assert.NotNull(forYou.Tracks);

        var ordered = forYou.Tracks!.Select(item => item.Track).ToList();

        var firstPreferred = ordered.FindIndex(track => track.ArtistId == library.Artist(0));
        var firstRejected = ordered.FindIndex(
            track => track.Id == library.Track(15) || track.Id == library.Track(16));

        Assert.True(firstPreferred >= 0, "Nothing from the artist the listener played made the shelf");
        Assert.True(
            firstRejected < 0 || firstPreferred < firstRejected,
            "An abandoned track outranked the artist the listener actually played");
    }

    [Fact]
    public async Task An_artist_sharing_tags_with_a_loved_one_becomes_a_candidate()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        // Артисты 0 и 3 не связаны ничем, кроме тегов: разные жанры, альбомы и годы.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var artistId in new[] { library.Artist(0), library.Artist(3) })
            {
                db.ArtistTags.AddRange(
                    new ArtistTag { ArtistId = artistId, Name = "shoegaze", Weight = 1.0 },
                    new ArtistTag { ArtistId = artistId, Name = "dream pop", Weight = 0.85 },
                    new ArtistTag { ArtistId = artistId, Name = "noise pop", Weight = 0.7 });
            }

            await db.SaveChangesAsync(Cancel.Token);
        }

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Completed(library.Track(1)),
            Liked(library.Track(2)));

        await WaitForEventsAsync(3);
        await RollupAsync(library.UserId);

        // Схожесть треков намеренно не пересчитываем: иначе источник «похоже на недавнее» разобрал
        // бы тех же кандидатов раньше и проверять было бы нечего.
        using var candidateScope = fixture.CreateScope();
        var generator = candidateScope.ServiceProvider.GetRequiredService<CandidateGenerator>();

        var context = await generator.LoadContextAsync(library.UserId, DateTimeOffset.UtcNow, Cancel.Token);
        var candidates = await generator.GenerateAsync(context, Cancel.Token);

        var fromTags = candidates
            .Where(candidate => candidate.Source == CandidateSource.SimilarArtists)
            .ToList();

        Assert.NotEmpty(fromTags);
        Assert.All(fromTags, candidate => Assert.Equal(library.Artist(3), candidate.ArtistId));
        Assert.All(fromTags, candidate => Assert.Equal(ReasonKinds.SimilarTo, candidate.ReasonKind));
        Assert.All(fromTags, candidate => Assert.Equal(library.Artist(3), candidate.ReasonSubjectId));
    }

    [Fact]
    public async Task Only_the_shelf_that_matches_the_listeners_clock_is_served()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client,
            Completed(library.Track(0)),
            Completed(library.Track(1)),
            Completed(library.Track(6)));

        await WaitForEventsAsync(3);

        // Настройки по умолчанию держат UTC, поэтому местное время слушателя — это now().
        var now = DateTimeOffset.UtcNow;
        var current = Dayparts.Of(now, TimeZoneInfo.Utc);
        var other = Dayparts.All.First(part => part != current);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Прослушивания раскладываются по двум частям суток поровну: полки соберутся обе,
            // а отдана должна быть одна.
            Listen(db, library, current, library.Track(0), now);
            Listen(db, library, current, library.Track(1), now);
            Listen(db, library, other, library.Track(6), now);
            Listen(db, library, other, library.Track(7), now);

            await db.SaveChangesAsync(Cancel.Token);
        }

        await fixture.BuildRecommendationsAsync(library.UserId);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var profile = await db.UserTasteProfiles.AsNoTracking()
                .FirstAsync(item => item.UserId == library.UserId, Cancel.Token);

            Assert.Equal(2, profile.Dayparts.Count);

            var cached = await db.RecommendationCache.AsNoTracking()
                .Where(entry => entry.UserId == library.UserId)
                .Select(entry => entry.ShelfKey)
                .ToListAsync(Cancel.Token);

            Assert.Contains(ShelfKeys.Of(current), cached);
            Assert.Contains(ShelfKeys.Of(other), cached);
        }

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home", Cancel.Token);

        var served = home!.Sections.Select(section => section.BaseKey).ToList();

        Assert.Contains(ShelfKeys.Of(current), served);
        Assert.DoesNotContain(ShelfKeys.Of(other), served);
    }

    private static void Listen(
        ApplicationDbContext db, SeededLibrary library, Daypart part, Guid trackId, DateTimeOffset now)
    {
        // Час подбирается внутри нужной части суток и в пределах окна, за которое собирается вкус.
        var hour = Enumerable.Range(0, 24).First(candidate => Dayparts.Of(candidate) == part);
        var day = now.UtcDateTime.Date.AddDays(-3);

        db.ListeningStats.Add(new ListeningStat
        {
            UserId = library.UserId,
            TrackId = trackId,
            Hour = new DateTimeOffset(day.AddHours(hour), TimeSpan.Zero),
            PlayCount = 6,
            ListenedSeconds = 1800,
        });
    }

    [Fact]
    public async Task No_single_artist_dominates_a_shelf()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 14, tracksPerArtist: 4);

        var events = Enumerable.Range(0, 5).Select(index => Completed(library.Track(index))).ToArray();
        await PostEventsAsync(client, events);
        await WaitForEventsAsync(events.Length);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        var forYou = home!.Sections.First(s => s.BaseKey == ShelfKeys.ForYou);

        var perArtist = forYou.Tracks!
            .GroupBy(item => item.Track.ArtistId)
            .Select(group => group.Count())
            .Max();

        using var scope = fixture.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Application.Options.RecommendationOptions>>();

        var breakdown = string.Join(", ", forYou.Tracks!
            .GroupBy(item => item.Track.ArtistName)
            .Select(group => $"{group.Key}={group.Count()}"));

        Assert.True(
            perArtist <= options.Value.MaxPerArtist,
            $"One artist took {perArtist} of {forYou.Tracks!.Count} slots ({breakdown})");
    }

    [Fact]
    public async Task A_user_with_no_history_still_gets_a_home_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(home);
        Assert.True(home.IsColdStart);
        Assert.NotEmpty(home.Sections);

        Assert.Contains(
            home.Sections,
            section => section.BaseKey is ShelfKeys.NewReleases or ShelfKeys.Discover or ShelfKeys.Popular);
    }

    [Fact]
    public async Task A_user_with_a_single_play_gets_recommendations()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        await PostEventsAsync(client, Completed(library.Track(0)));
        await WaitForEventsAsync(1);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var home = await client.GetFromJsonAsync<RecommendationHomeDto>(
            "/api/recommendations/home?sectionSize=12", Cancel.Token);

        Assert.NotNull(home);
        Assert.NotEmpty(home.Sections);

        var tracks = home.Sections.Where(s => s.Tracks is not null).SelectMany(s => s.Tracks!).ToList();
        Assert.NotEmpty(tracks);
    }

    [Fact]
    public async Task Only_skipping_does_not_break_the_page()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync();

        var events = Enumerable.Range(0, 12)
            .Select(index => Skipped(library.Track(index), listened: 2))
            .ToArray();

        await PostEventsAsync(client, events);
        await WaitForEventsAsync(events.Length);
        await fixture.BuildRecommendationsAsync(library.UserId);

        var response = await client.GetAsync("/api/recommendations/home?sectionSize=12", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var home = await response.Content.ReadFromJsonAsync<RecommendationHomeDto>(Cancel.Token);
        Assert.NotNull(home);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var negative = await db.UserTrackAffinities.AsNoTracking()
            .CountAsync(a => a.UserId == library.UserId && a.Score < 0, Cancel.Token);

        Assert.Equal(12, negative);
    }


    private static object Completed(Guid trackId) => new
    {
        type = "trackCompleted",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        positionSeconds = 200,
        listenedSeconds = 200,
        durationSeconds = 200,
        sessionId = Session,
        source = "home",
        platform = "web",
    };

    private static object Skipped(Guid trackId, int listened) => new
    {
        type = "trackSkipped",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        positionSeconds = listened,
        listenedSeconds = listened,
        durationSeconds = 200,
        sessionId = Session,
        source = "home",
        platform = "web",
    };

    private static object Liked(Guid trackId) => new
    {
        type = "trackLiked",
        trackId,
        occurredAt = DateTimeOffset.UtcNow,
        sessionId = Session,
        source = "home",
        platform = "web",
    };

    private static readonly Guid Session = Guid.CreateVersion7();

    private static async Task PostEventsAsync(HttpClient client, params object[] events)
    {
        var response = await client.PostAsJsonAsync("/api/events", new { events });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task WaitForEventsAsync(int expected)
    {
        var deadline = DateTimeOffset.UtcNow + IngestTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (await db.PlaybackEvents.CountAsync() >= expected)
                return;

            await Task.Delay(100);
        }

        Assert.Fail($"Only {await CountEventsAsync()} of {expected} events were written within {IngestTimeout}.");
    }

    private async Task<int> CountEventsAsync()
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .PlaybackEvents.CountAsync();
    }

    private async Task RollupAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ProfileRollupService>().RollupAsync(userId);
    }

}
