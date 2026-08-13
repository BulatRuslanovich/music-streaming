using System.Net;
using System.Net.Http.Json;
using MusicStreaming.Application.Dtos;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Публичный плейлист виден всем, но принадлежит по-прежнему одному. Здесь проверяется обе половины
/// этого: что до переключения флага чужой плейлист не достать ни списком, ни по прямой ссылке, и что
/// после переключения он читается всеми, оставаясь недоступным для изменения кому-либо, кроме
/// владельца. Вторая половина важнее: открыть чтение легко так, что заодно откроется и запись.
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class PublicPlaylistTests(RecommendationApiFixture fixture)
{
    private const string ListenerUsername = "listener";
    private const string ListenerPassword = "integration-password-listener";

    [Fact]
    public async Task A_private_playlist_stays_invisible_to_other_users()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var owner = await fixture.CreateSignedInClientAsync();
        var listener = await fixture.CreateSignedInClientAsync(ListenerUsername, ListenerPassword);

        var playlist = await CreatePlaylistAsync(owner, "Private mix", isPublic: false);

        var direct = await listener.GetAsync($"/api/playlists/{playlist.Id}");
        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);

        var shelf = await GetPublicAsync(listener);
        Assert.DoesNotContain(shelf, p => p.Id == playlist.Id);
    }

    [Fact]
    public async Task A_public_playlist_is_readable_by_other_users()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var owner = await fixture.CreateSignedInClientAsync();
        var listener = await fixture.CreateSignedInClientAsync(ListenerUsername, ListenerPassword);

        var playlist = await CreatePlaylistAsync(owner, "Shared mix", isPublic: true);

        var detail = await listener.GetFromJsonAsync<PlaylistDetailDto>($"/api/playlists/{playlist.Id}");
        Assert.NotNull(detail);
        Assert.True(detail.IsPublic);
        Assert.Equal(RecommendationApiFixture.OwnerUsername, detail.OwnerName);

        var shelf = await GetPublicAsync(listener);
        Assert.Contains(shelf, p => p.Id == playlist.Id);

        // Витрина — не библиотека: чужой публичный плейлист не должен попадать в «свои».
        var mine = await listener.GetFromJsonAsync<List<PlaylistDto>>("/api/playlists");
        Assert.NotNull(mine);
        Assert.DoesNotContain(mine, p => p.Id == playlist.Id);
    }

    [Fact]
    public async Task Only_the_owner_can_change_a_public_playlist()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var owner = await fixture.CreateSignedInClientAsync();
        var listener = await fixture.CreateSignedInClientAsync(ListenerUsername, ListenerPassword);

        var playlist = await CreatePlaylistAsync(owner, "Read-only mix", isPublic: true);

        var renamed = await listener.PutAsJsonAsync(
            $"/api/playlists/{playlist.Id}",
            new { name = "Hijacked", description = (string?)null, isPublic = true });

        Assert.Equal(HttpStatusCode.NotFound, renamed.StatusCode);

        var reordered = await listener.PutAsJsonAsync(
            $"/api/playlists/{playlist.Id}/tracks/order", new { trackIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.NotFound, reordered.StatusCode);

        var deleted = await listener.DeleteAsync($"/api/playlists/{playlist.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        var still = await owner.GetFromJsonAsync<PlaylistDetailDto>($"/api/playlists/{playlist.Id}");
        Assert.NotNull(still);
        Assert.Equal("Read-only mix", still.Name);
    }

    [Fact]
    public async Task Publishing_and_unpublishing_take_effect_immediately()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var owner = await fixture.CreateSignedInClientAsync();
        var listener = await fixture.CreateSignedInClientAsync(ListenerUsername, ListenerPassword);

        var playlist = await CreatePlaylistAsync(owner, "Switching mix", isPublic: false);

        await UpdateAsync(owner, playlist.Id, "Switching mix", isPublic: true);
        Assert.Contains(await GetPublicAsync(listener), p => p.Id == playlist.Id);

        await UpdateAsync(owner, playlist.Id, "Switching mix", isPublic: false);
        Assert.DoesNotContain(await GetPublicAsync(listener), p => p.Id == playlist.Id);

        var direct = await listener.GetAsync($"/api/playlists/{playlist.Id}");
        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
    }

    private static async Task<PlaylistDto> CreatePlaylistAsync(HttpClient client, string name, bool isPublic)
    {
        var response = await client.PostAsJsonAsync(
            "/api/playlists", new { name, description = (string?)null, isPublic });

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<PlaylistDto>();
        Assert.NotNull(created);
        Assert.Equal(isPublic, created.IsPublic);

        return created;
    }

    private static async Task UpdateAsync(HttpClient client, Guid id, string name, bool isPublic)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/playlists/{id}", new { name, description = (string?)null, isPublic });

        response.EnsureSuccessStatusCode();
    }

    private static async Task<List<PlaylistDto>> GetPublicAsync(HttpClient client)
    {
        var shelf = await client.GetFromJsonAsync<List<PlaylistDto>>("/api/playlists/public");
        Assert.NotNull(shelf);
        return shelf;
    }
}
