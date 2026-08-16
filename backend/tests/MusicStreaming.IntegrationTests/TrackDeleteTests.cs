using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Удаление треков — и поштучное, и набором.
///
/// <para>
/// Проверяется не только то, что строки исчезли: удаление уносит с собой файл из хранилища, а
/// вместе с последним треком — опустевшие альбом, исполнителя и жанр. Всё зависимое (плейлисты,
/// избранное, история) держится на каскадах самой базы, а не на коде, поэтому один из тестов
/// смотрит именно на них.
/// </para>
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class TrackDeleteTests(RecommendationApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_batch_takes_its_files_albums_artists_and_genres_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Batch");

        var uploaded = await UploadAsync(client, [
            Mp3($"{name}-1.mp3", $"{name} One", $"{name} Artist", $"{name} Album", $"{name} Genre", 1),
            Mp3($"{name}-2.mp3", $"{name} Two", $"{name} Artist", $"{name} Album", $"{name} Genre", 2),
            Mp3($"{name}-3.mp3", $"{name} Three", $"{name} Other", $"{name} Second", $"{name} Genre", 1),
        ]);

        Assert.Equal(3, uploaded.Uploaded.Count);
        var paths = await FilePathsAsync([.. uploaded.Uploaded.Select(t => t.Id)]);
        Assert.All(paths, path => Assert.NotNull(Resolve(path)));

        var result = await DeleteAsync(client, [.. uploaded.Uploaded.Select(t => t.Id)]);

        Assert.Equal(3, result.Deleted);
        Assert.Empty(result.Missing);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(await FilePathsAsync([.. uploaded.Uploaded.Select(t => t.Id)]));

        // Опустевшие альбомы, исполнители и жанр уходят следом — за один проход на весь набор.
        Assert.Equal(0, await db.Albums.CountAsync(a => a.Title.StartsWith(name), Cancel.Token));
        Assert.Equal(0, await db.Artists.CountAsync(a => a.Name.StartsWith(name), Cancel.Token));
        Assert.Equal(0, await db.Genres.CountAsync(g => g.Name.StartsWith(name), Cancel.Token));

        // И сами файлы, а не только строки о них.
        Assert.All(paths, path => Assert.Null(Resolve(path)));
    }

    /// <summary>
    /// Удалить уже удалённое — не ошибка: запрос шёл к тому состоянию, в котором библиотека и так
    /// находится. Поэтому неизвестные идентификаторы возвращаются списком, а не отказом, и остальное
    /// из того же набора уходит как ни в чём не бывало.
    /// </summary>
    [Fact]
    public async Task Ids_that_were_already_gone_come_back_as_missing_and_do_not_stop_the_rest()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Missing");

        var uploaded = await UploadAsync(client, [
            Mp3($"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, 1),
        ]);

        var alive = Assert.Single(uploaded.Uploaded).Id;
        var ghost = Guid.CreateVersion7();

        var result = await DeleteAsync(client, [alive, ghost]);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(ghost, Assert.Single(result.Missing));

        // Второй заход по тому же набору не находит уже ничего — и всё равно отвечает успехом.
        var again = await DeleteAsync(client, [alive, ghost]);

        Assert.Equal(0, again.Deleted);
        Assert.Equal(2, again.Missing.Count);
    }

    /// <summary>
    /// Плейлисты, избранное и история очищаются каскадами самой базы, а не кодом сервиса. Пакетное
    /// удаление сносит строки одним оператором в обход трекера изменений, поэтому убедиться, что
    /// каскады при этом срабатывают, стоит отдельно.
    /// </summary>
    [Fact]
    public async Task A_track_deleted_in_a_batch_leaves_no_playlist_entry_or_favorite_behind()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Linked");

        var uploaded = await UploadAsync(client, [
            Mp3($"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, 1),
        ]);

        var trackId = Assert.Single(uploaded.Uploaded).Id;

        var playlist = await client.PostAsJsonAsync(
            "/api/playlists", new { name = $"{name} Playlist", isPublic = false }, Cancel.Token);
        playlist.EnsureSuccessStatusCode();

        var playlistId = (await playlist.Content.ReadFromJsonAsync<PlaylistDto>(Json, Cancel.Token))!.Id;

        (await client.PostAsJsonAsync(
            $"/api/playlists/{playlistId}/tracks", new { trackId }, Cancel.Token)).EnsureSuccessStatusCode();

        (await client.PostAsync($"/api/tracks/{trackId}/favorite", null, Cancel.Token)).EnsureSuccessStatusCode();

        using (var before = fixture.CreateScope())
        {
            var db = before.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.PlaylistTracks.CountAsync(pt => pt.TrackId == trackId, Cancel.Token));
            Assert.Equal(1, await db.Favorites.CountAsync(f => f.TrackId == trackId, Cancel.Token));
        }

        Assert.Equal(1, (await DeleteAsync(client, [trackId])).Deleted);

        using var after = fixture.CreateScope();
        var context = after.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, await context.PlaylistTracks.CountAsync(pt => pt.TrackId == trackId, Cancel.Token));
        Assert.Equal(0, await context.Favorites.CountAsync(f => f.TrackId == trackId, Cancel.Token));

        // Сам плейлист остаётся — исчезает только запись в нём.
        Assert.Equal(1, await context.Playlists.CountAsync(p => p.Id == playlistId, Cancel.Token));
    }

    [Fact]
    public async Task A_batch_larger_than_the_cap_is_refused()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var ids = Enumerable.Range(0, TrackEditService.MaxBulkDelete + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        var response = await client.PostAsJsonAsync(
            "/api/tracks/bulk-delete", new { ids }, Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_selection_is_refused()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/tracks/bulk-delete", new { ids = Array.Empty<Guid>() }, Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_delete_is_closed_to_everyone_but_administrators()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var owner = await fixture.CreateSignedInClientAsync();
        var name = Unique("Guarded");

        var uploaded = await UploadAsync(owner, [
            Mp3($"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, 1),
        ]);

        var trackId = Assert.Single(uploaded.Uploaded).Id;

        var listener = await fixture.CreateSignedInClientAsync($"listener-{Guid.CreateVersion7():N}"[..20], "12345678");

        var response = await listener.PostAsJsonAsync(
            "/api/tracks/bulk-delete", new { ids = new[] { trackId } }, Cancel.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Tracks.CountAsync(t => t.Id == trackId, Cancel.Token));
    }

    /// <summary>Поштучное удаление ходит теперь через тот же путь, и его ответ на небылицу — по-прежнему 404.</summary>
    [Fact]
    public async Task Deleting_a_track_that_does_not_exist_is_still_a_404()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        var response = await client.DeleteAsync($"/api/tracks/{Guid.CreateVersion7()}", Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Вспомогательное ─────────────────────────────────────────────────────────────────────

    private static string Unique(string prefix) => $"{prefix} {Guid.CreateVersion7():N}"[..24];

    private static AudioFormatTests.UploadFile Mp3(
        string fileName, string title, string artist, string? album, string? genre, int track) =>
        AudioFormatTests.SyntheticMp3.Tagged(fileName, title, artist, album, genre, null, track);

    private string? Resolve(string relativePath)
    {
        using var scope = fixture.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMusicStorage>().ResolveExisting(relativePath);
    }

    private async Task<List<string>> FilePathsAsync(IReadOnlyList<Guid> ids)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Tracks.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => t.FilePath)
            .ToListAsync(Cancel.Token);
    }

    private static async Task<BulkDeleteResultDto> DeleteAsync(HttpClient client, IReadOnlyList<Guid> ids)
    {
        var response = await client.PostAsJsonAsync("/api/tracks/bulk-delete", new { ids }, Cancel.Token);

        Assert.True(
            response.IsSuccessStatusCode,
            $"unexpected status {response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel.Token)}");

        return (await response.Content.ReadFromJsonAsync<BulkDeleteResultDto>(Json, Cancel.Token))!;
    }

    private static async Task<UploadResultDto> UploadAsync(
        HttpClient client, IReadOnlyList<AudioFormatTests.UploadFile> files)
    {
        using var form = new MultipartFormDataContent();

        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Content);

            if (file.ContentType is not null)
                content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            form.Add(content, "files", file.FileName);
        }

        var response = await client.PostAsync("/api/tracks/upload", form, Cancel.Token);
        return (await response.Content.ReadFromJsonAsync<UploadResultDto>(Json, Cancel.Token))!;
    }
}
