// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class MonthlyRecapTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_recap_scopes_calendar_month_and_discoveries_to_the_current_listener()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);
        var (library, client) = await fixture.SeedAndSignInAsync();
        var otherClient = await fixture.CreateSignedInClientAsync("recap-other", "recap-other-password");
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherId = await db.Users.Where(u => u.Username == "recap-other").Select(u => u.Id).SingleAsync(Cancel.Token);
            db.ListeningStats.AddRange(
                Stat(library.UserId, library.Track(0), "2026-07-31T20:00:00Z", 100),
                Stat(library.UserId, library.Track(0), "2026-07-31T21:00:00Z", 200),
                Stat(library.UserId, library.Track(1), "2026-08-15T12:00:00Z", 300),
                Stat(library.UserId, library.Track(10), "2026-08-16T12:00:00Z", 50),
                Stat(library.UserId, library.Track(2), "2026-08-31T21:00:00Z", 900),
                Stat(otherId, library.Track(0), "2026-08-15T12:00:00Z", 700));
            await db.SaveChangesAsync(Cancel.Token);
        }
        (await client.PutAsJsonAsync("/api/me/settings", new { TimeZone = "Europe/Moscow" }, Cancel.Token)).EnsureSuccessStatusCode();
        var response = await client.GetAsync("/api/me/recap?month=2026-08", Cancel.Token);
        var body = await response.Content.ReadAsStringAsync(Cancel.Token);
        Assert.True(response.IsSuccessStatusCode, body);
        var recap = System.Text.Json.JsonSerializer.Deserialize<MonthlyRecapDto>(body, RecommendationApiFixture.Json)!;
        Assert.Equal(550, recap.ListenedSeconds);
        Assert.Equal(100, recap.PreviousListenedSeconds);
        Assert.Equal(3, recap.UniqueTracks);
        Assert.DoesNotContain(recap.Discoveries, a => a.Id == library.Artist(0) || a.Id == library.Artist(1));
        Assert.Equal(library.Artist(2), Assert.Single(recap.Discoveries).Id);
        var empty = await otherClient.GetFromJsonAsync<MonthlyRecapDto>("/api/me/recap?month=2026-06", RecommendationApiFixture.Json, Cancel.Token);
        Assert.Equal(0, empty!.ListenedSeconds);

        var saved = await client.PostAsJsonAsync("/api/me/recap/playlist", new { Month = "2026-08", Name = "August" }, Cancel.Token);
        saved.EnsureSuccessStatusCode();
        using var verify = fixture.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var playlist = await context.Playlists.Include(p => p.Tracks)
            .SingleAsync(p => p.UserId == library.UserId && p.Name == "August", Cancel.Token);
        Assert.False(playlist.IsPublic);
        Assert.Equal(recap.TopTracks.Select(t => t.Track.Id), playlist.Tracks.OrderBy(t => t.Position).Select(t => t.TrackId));
    }

    private static ListeningStat Stat(Guid user, Guid track, string hour, long seconds) => new()
    {
        UserId = user,
        TrackId = track,
        Hour = DateTimeOffset.Parse(hour),
        ListenedSeconds = seconds,
        PlayCount = 1,
    };
}
