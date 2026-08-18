using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class LyricsTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_track_with_no_lyrics_answers_with_no_content()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        var response = await client.GetAsync($"/api/tracks/{library.Track(0)}/lyrics", Cancel.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Pasted_lrc_becomes_synchronised_lyrics()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();

        var saved = await ReplaceAsync(
            client, library.Track(0), "[ar:Someone]\n[00:10.00]First line\n[00:20.50]Second line");

        Assert.Equal(2, saved!.Lines.Count);
        Assert.Equal(10_000, saved.Lines[0].At);
        Assert.Equal(20_500, saved.Lines[1].At);
        Assert.Equal("First line\nSecond line", saved.Plain);
        Assert.Equal(LyricsSource.Manual, saved.Source);
    }

    [Fact]
    public async Task Pasted_plain_text_stays_plain()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        var saved = await ReplaceAsync(client, library.Track(0), "Just words\nAnd more words");

        Assert.Empty(saved!.Lines);
        Assert.Equal("Just words\nAnd more words", saved.Plain);
    }

    [Fact]
    public async Task Saved_lyrics_are_visible_to_every_listener_and_flagged_on_the_track()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await ReplaceAsync(client, library.Track(0), "[00:01.00]A line");

        var fetched = await client.GetFromJsonAsync<LyricsDto>(
            $"/api/tracks/{library.Track(0)}/lyrics", RecommendationApiFixture.Json, Cancel.Token);
        Assert.Equal("A line", fetched!.Plain);

        var track = await client.GetFromJsonAsync<TrackDto>($"/api/tracks/{library.Track(0)}", Cancel.Token);
        Assert.True(track!.HasLyrics);

        var other = await client.GetFromJsonAsync<TrackDto>($"/api/tracks/{library.Track(1)}", Cancel.Token);
        Assert.False(other!.HasLyrics);
    }

    [Fact]
    public async Task Saving_empty_text_removes_the_lyrics()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await ReplaceAsync(client, library.Track(0), "Something wrong from a bad tag");

        var cleared = await client.PutAsJsonAsync(
            $"/api/tracks/{library.Track(0)}/lyrics", new UpdateLyricsRequest("   "), Cancel.Token);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.GetAsync($"/api/tracks/{library.Track(0)}/lyrics", Cancel.Token)).StatusCode);
    }

    [Fact]
    public async Task Editing_lyrics_is_for_administrators_only()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, admin) = await StartAsync();
        var listener = await fixture.CreateSignedInClientAsync("lyrics-listener", "listener-password");

        var attempt = await listener.PutAsJsonAsync(
            $"/api/tracks/{library.Track(0)}/lyrics", new UpdateLyricsRequest("Sneaky"), Cancel.Token);

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);

        await ReplaceAsync(admin, library.Track(0), "Public words");
        var read = await listener.GetFromJsonAsync<LyricsDto>(
            $"/api/tracks/{library.Track(0)}/lyrics", RecommendationApiFixture.Json, Cancel.Token);

        Assert.Equal("Public words", read!.Plain);
    }

    [Fact]
    public async Task Lyrics_for_a_track_that_does_not_exist_are_refused()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/tracks/{Guid.CreateVersion7()}/lyrics", new UpdateLyricsRequest("Words"), Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_track_takes_its_lyrics_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await ReplaceAsync(client, library.Track(0), "Words");

        (await client.DeleteAsync($"/api/tracks/{library.Track(0)}", Cancel.Token)).EnsureSuccessStatusCode();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(db.TrackLyrics.Where(l => l.TrackId == library.Track(0)));
    }

    private static async Task<LyricsDto?> ReplaceAsync(HttpClient client, Guid trackId, string text)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/tracks/{trackId}/lyrics", new UpdateLyricsRequest(text));

        response.EnsureSuccessStatusCode();

        return response.StatusCode == HttpStatusCode.NoContent
            ? null
            : await response.Content.ReadFromJsonAsync<LyricsDto>(RecommendationApiFixture.Json);
    }

    private async Task<(SeededLibrary Library, HttpClient Client)> StartAsync()
    {
        var client = await fixture.CreateSignedInClientAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return (await LibrarySeeder.SeedAsync(db), client);
    }
}
