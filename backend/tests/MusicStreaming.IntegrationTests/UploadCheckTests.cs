using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Проверка перед загрузкой: клиент называет хеш и теги, сервер отвечает, что из этого уже есть.
/// Смысл её в том, чтобы совпавший файл вообще не пересекал сеть, поэтому ошибка здесь стоит
/// напрасно загруженного файла — а на медленном канале это единственное, что пользователь заметит.
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class UploadCheckTests(RecommendationApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Настоящий хеш — 64 шестнадцатеричных знака; засеянные треки такими не размечены.</summary>
    private const string KnownHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task A_file_the_library_already_has_is_recognised_by_its_hash()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        var trackId = await StampHashAsync(library.Track(0), KnownHash);

        var result = await CheckAsync(client, new UploadProbeFileDto("whatever.mp3", KnownHash, null, null));

        var verdict = Assert.Single(result.Files);
        Assert.Equal(UploadProbeVerdict.Duplicate, verdict.Verdict);
        Assert.Equal(trackId, verdict.Match?.Id);
    }

    /// <summary>
    /// Перекодированный или перетегированный файл побайтово не совпадёт никогда, а песня та же —
    /// поэтому одного хеша мало и теги проверяются отдельно.
    /// </summary>
    [Fact]
    public async Task The_same_song_in_a_different_file_is_recognised_by_its_tags()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        var track = await LoadTrackAsync(library.Track(3));

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("rip.mp3", null, track.Title, track.ArtistName));

        var verdict = Assert.Single(result.Files);
        Assert.Equal(UploadProbeVerdict.Similar, verdict.Verdict);
        Assert.Equal(track.Id, verdict.Match?.Id);
    }

    /// <summary>Регистр и лишние пробелы в тегах — не повод считать песню другой.</summary>
    [Fact]
    public async Task Tags_are_matched_the_way_the_library_normalises_them()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        var track = await LoadTrackAsync(library.Track(2));

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("rip.mp3", null, $"  {track.Title.ToUpperInvariant()} ", track.ArtistName));

        Assert.Equal(UploadProbeVerdict.Similar, Assert.Single(result.Files).Verdict);
    }

    /// <summary>Одного названия мало: разные исполнители играют песни с одинаковыми названиями.</summary>
    [Fact]
    public async Task A_matching_title_alone_is_not_enough()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        var track = await LoadTrackAsync(library.Track(1));

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("rip.mp3", null, track.Title, "Somebody Else Entirely"),
            new UploadProbeFileDto("untagged.mp3", null, track.Title, null));

        Assert.All(result.Files, file => Assert.Equal(UploadProbeVerdict.New, file.Verdict));
    }

    [Fact]
    public async Task An_unknown_file_is_reported_as_new()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("new.mp3", new string('f', 64), "Nothing Like It", "Nobody At All"));

        var verdict = Assert.Single(result.Files);
        Assert.Equal(UploadProbeVerdict.New, verdict.Verdict);
        Assert.Null(verdict.Match);
    }

    /// <summary>
    /// Испорченный хеш не должен ронять проверку: файл просто уйдёт на сервер, где дубликат
    /// поймается по-старому, уже после загрузки.
    /// </summary>
    [Fact]
    public async Task A_malformed_hash_is_ignored_rather_than_rejected()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await StampHashAsync(library.Track(0), KnownHash);

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("odd.mp3", "not-a-hash", null, null),
            new UploadProbeFileDto("odd2.mp3", KnownHash.ToUpperInvariant(), null, null));

        Assert.Equal(UploadProbeVerdict.New, result.Files[0].Verdict);

        // Регистр — единственная вольность, которую разбор хеша себе позволяет.
        Assert.Equal(UploadProbeVerdict.Duplicate, result.Files[1].Verdict);
    }

    /// <summary>Ответ сопоставляется с запросом по позиции, поэтому порядок и длина обязаны совпасть.</summary>
    [Fact]
    public async Task Answers_come_back_in_the_order_they_were_asked()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await StartAsync();
        await StampHashAsync(library.Track(0), KnownHash);

        var result = await CheckAsync(
            client,
            new UploadProbeFileDto("a.mp3", null, "Nothing Like It", "Nobody At All"),
            new UploadProbeFileDto("b.mp3", KnownHash, null, null),
            new UploadProbeFileDto("c.mp3", null, null, null));

        Assert.Equal(["a.mp3", "b.mp3", "c.mp3"], result.Files.Select(f => f.FileName));
        Assert.Equal(
            [UploadProbeVerdict.New, UploadProbeVerdict.Duplicate, UploadProbeVerdict.New],
            result.Files.Select(f => f.Verdict));
    }

    [Fact]
    public async Task An_empty_request_asks_nothing_of_the_database()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (_, client) = await StartAsync();

        var result = await CheckAsync(client);

        Assert.Empty(result.Files);
    }

    private static async Task<UploadProbeResultDto> CheckAsync(
        HttpClient client, params UploadProbeFileDto[] files)
    {
        var response = await client.PostAsJsonAsync(
            "/api/tracks/upload/check", new UploadProbeRequest(files), Json);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<UploadProbeResultDto>(Json))!;
    }

    private async Task<(SeededLibrary Library, HttpClient Client)> StartAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var library = await LibrarySeeder.SeedAsync(db);
        return (library, await fixture.CreateSignedInClientAsync());
    }

    private async Task<Guid> StampHashAsync(Guid trackId, string hash)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Tracks.Where(t => t.Id == trackId).ExecuteUpdateAsync(
            update => update.SetProperty(t => t.ContentHash, hash));

        return trackId;
    }

    private async Task<(Guid Id, string Title, string ArtistName)> LoadTrackAsync(Guid trackId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var track = await db.Tracks
            .Where(t => t.Id == trackId)
            .Select(t => new { t.Id, t.Title, ArtistName = t.Artist!.Name })
            .SingleAsync();

        return (track.Id, track.Title, track.ArtistName);
    }
}
