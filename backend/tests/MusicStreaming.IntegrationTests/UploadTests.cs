using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Common;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Сама загрузка — от многочастного запроса до строк в базе.
///
/// <para>
/// Отдельно от <see cref="UploadCheckTests"/>: тот проверяет только предварительный опрос по тегам
/// и хешу, а здесь через <c>TrackUploadService</c> и <c>TagResolver</c> проходят настоящие файлы.
/// Именно этот путь заводит исполнителей, альбомы и жанры, и именно на нём пакет из нескольких
/// файлов одного альбома обязан свести их к одной строке на каждого, а не завести повторно — на
/// <c>normalized_name</c> стоит уникальный индекс, и вторая попытка была бы не дубликатом, а
/// упавшим сохранением.
/// </para>
/// </summary>
[Collection(nameof(RecommendationApiCollection))]
public class UploadTests(RecommendationApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task An_uploaded_file_becomes_a_track_with_the_metadata_from_its_tags()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Solo");

        var result = await UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}.mp3", title: $"{name} Title", artist: $"{name} Artist",
                album: $"{name} Album", genre: $"{name} Genre", year: 1999, track: 7),
        ]);

        Assert.Empty(result.Failed);
        var uploaded = Assert.Single(result.Uploaded);

        Assert.Equal($"{name} Title", uploaded.Title);
        Assert.Equal($"{name} Artist", uploaded.ArtistName);
        Assert.Equal($"{name} Album", uploaded.AlbumTitle);
        Assert.Equal($"{name} Genre", uploaded.GenreName);
        Assert.Equal(1999, uploaded.Year);
        Assert.Equal(7, uploaded.TrackNumber);
        Assert.True(uploaded.DurationSeconds > 0, "the synthetic file should carry a readable duration");
    }

    /// <summary>
    /// Главная проверка пакетной загрузки: несколько файлов одного альбома складываются в одного
    /// исполнителя, один альбом и один жанр. Раньше это обеспечивалось сохранением после каждого
    /// файла; теперь — памятью резолвера, и сломать это значит либо получить дубликаты, либо
    /// упереться в уникальный индекс.
    /// </summary>
    [Fact]
    public async Task A_batch_from_one_album_creates_its_artist_album_and_genre_exactly_once()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Batch");

        var files = Enumerable.Range(1, 5)
            .Select(number => SyntheticMp3.Tagged(
                $"{name}-{number}.mp3",
                title: $"{name} Track {number}",
                artist: $"{name} Artist",
                album: $"{name} Album",
                genre: $"{name} Genre",
                year: 2001,
                track: number))
            .ToList();

        var result = await UploadAsync(client, files);

        Assert.Empty(result.Failed);
        Assert.Equal(5, result.Uploaded.Count);

        // Все пять треков должны указывать на один и тот же альбом и одного исполнителя.
        Assert.Single(result.Uploaded.Select(t => t.AlbumId).Distinct());
        Assert.Single(result.Uploaded.Select(t => t.ArtistId).Distinct());

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Artist"), Cancel.Token));
        Assert.Equal(1, await db.Albums.CountAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Album"), Cancel.Token));
        Assert.Equal(1, await db.Genres.CountAsync(
            g => g.NormalizedName == Normalize.Key($"{name} Genre"), Cancel.Token));
    }

    /// <summary>
    /// Тот же исполнитель, пришедший вторым запросом, тоже не заводится повторно: память резолвера
    /// живёт один запрос, дальше его обязан находить обычный поиск по базе.
    /// </summary>
    [Fact]
    public async Task An_artist_from_an_earlier_upload_is_reused_by_the_next_one()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Across");

        var first = await UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-a.mp3", $"{name} A", $"{name} Artist", $"{name} Album", null, null, 1),
        ]);

        var second = await UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-b.mp3", $"{name} B", $"{name} Artist", $"{name} Album", null, null, 2),
        ]);

        Assert.Empty(first.Failed);
        Assert.Empty(second.Failed);

        Assert.Equal(
            Assert.Single(first.Uploaded).ArtistId,
            Assert.Single(second.Uploaded).ArtistId);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Artist"), Cancel.Token));
    }

    /// <summary>
    /// Один и тот же новый исполнитель, названный и в списке исполнителей трека, и в исполнителях
    /// альбома, обязан остаться одной строкой.
    ///
    /// <para>
    /// Внутри одного файла резолвер спрашивают дважды, и до первого сохранения обычный поиск по базе
    /// заведённого этим же файлом исполнителя не видит. Раньше от повтора спасало сохранение между
    /// двумя вызовами, теперь — память резолвера; без неё файл упирается в уникальный индекс на
    /// <c>normalized_name</c> и отвергается целиком.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_artist_named_both_on_the_track_and_on_the_album_is_created_once()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Both");

        var result = await UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}.mp3", $"{name} Title", $"{name} Artist", $"{name} Album",
                null, null, 1, albumArtist: $"{name} Artist"),
        ]);

        Assert.Empty(result.Failed);
        Assert.Single(result.Uploaded);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Artist"), Cancel.Token));
    }

    /// <summary>Один негодный файл в пакете не должен уносить с собой остальные.</summary>
    [Fact]
    public async Task A_rejected_file_does_not_take_the_rest_of_the_batch_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Mixed");

        var result = await UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-good.mp3", $"{name} Good", $"{name} Artist", $"{name} Album", null, null, 1),
            new UploadFile($"{name}-bad.mp3", "audio/mpeg", [0x00, 0x01, 0x02, 0x03]),
            SyntheticMp3.Tagged($"{name}-also.mp3", $"{name} Also", $"{name} Artist", $"{name} Album", null, null, 2),
        ]);

        Assert.Equal(2, result.Uploaded.Count);
        Assert.Single(result.Failed);
        Assert.Equal($"{name}-bad.mp3", result.Failed[0].FileName);

        // Оба уцелевших трека всё ещё сведены к одному альбому — отказ посередине не должен
        // ни расщепить их, ни утащить в базу что-то от отвергнутого файла.
        Assert.Single(result.Uploaded.Select(t => t.AlbumId).Distinct());

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Albums.CountAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Album"), Cancel.Token));
    }

    /// <summary>
    /// Файл, упавший уже после того, как завёл своего исполнителя и альбом, не должен утащить их в
    /// базу вместе со следующим файлом пакета.
    /// </summary>
    ///
    /// <para>
    /// Каждый файл сохраняется одним разом в конце, поэтому незаписанное упавшего остаётся в
    /// трекере изменений и без явного отказа уехало бы со следующим сохранением. Сбой внедряется
    /// через обработчик картинок: только он вызывается уже после заведения сущностей, и только его
    /// отказ (не <c>ValidationException</c>) поднимается наружу, а не гасится на месте.
    /// </para>
    [Fact]
    public async Task A_file_that_fails_after_creating_its_tags_leaves_nothing_behind()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var name = Unique("Doomed");

        using var factory = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IImageProcessor>();
                services.AddSingleton<IImageProcessor, ExplodingImageProcessor>();
            }));

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = RecommendationApiFixture.OwnerUsername,
                password = RecommendationApiFixture.OwnerPassword,
            },
            Cancel.Token);

        login.EnsureSuccessStatusCode();

        var result = await UploadAsync(client, [
            // Первым — тот, что упадёт: его незаписанное поедет со вторым, если отказ не сработает.
            SyntheticMp3.Tagged($"{name}-bad.mp3", $"{name} Bad", $"{name} Doomed Artist",
                $"{name} Doomed Album", null, null, 1, cover: [1, 2, 3, 4]),
            SyntheticMp3.Tagged($"{name}-ok.mp3", $"{name} Ok", $"{name} Fine Artist",
                $"{name} Fine Album", null, null, 2),
        ]);

        Assert.Single(result.Failed);
        Assert.Equal($"{name}-bad.mp3", result.Failed[0].FileName);
        Assert.Single(result.Uploaded);
        Assert.Equal($"{name} Ok", result.Uploaded[0].Title);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Уцелевший файл на месте, а от упавшего не осталось ни строки.
        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Fine Artist"), Cancel.Token));

        Assert.Equal(0, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Doomed Artist"), Cancel.Token));

        Assert.Equal(0, await db.Albums.CountAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Doomed Album"), Cancel.Token));

        Assert.Equal(0, await db.Tracks.CountAsync(
            t => t.Title == $"{name} Bad", Cancel.Token));
    }

    /// <summary>Обработчик картинок, который всегда отказывает, — и отказывает не так, как отказал бы негодный файл.</summary>
    private sealed class ExplodingImageProcessor : IImageProcessor
    {
        public Task<byte[]> ToSquareWebpAsync(Stream source, int edge, CancellationToken ct = default) =>
            throw new InvalidOperationException("image processing is unavailable");

        public Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
            Stream source, IReadOnlyList<int> edges, CancellationToken ct = default) =>
            throw new InvalidOperationException("image processing is unavailable");
    }

    [Fact]
    public async Task The_same_file_twice_is_refused_as_a_duplicate()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Twice");
        var file = SyntheticMp3.Tagged($"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, null, 1);

        var first = await UploadAsync(client, [file]);
        var second = await UploadAsync(client, [file]);

        Assert.Single(first.Uploaded);
        Assert.Empty(second.Uploaded);
        Assert.Single(second.Failed);
    }

    /// <summary>
    /// Файлы одного альбома, приехавшие одновременно, сходятся к одному исполнителю, а не убивают
    /// друг друга об уникальный индекс.
    ///
    /// <para>
    /// Память <c>TagResolver</c> живёт ровно один запрос, поэтому договориться между собой
    /// параллельные загрузки не могут: каждая не находит исполнителя и заводит своего, а выживает
    /// одна. Спасает повтор после конфликта — проигравший переспрашивает и подбирает уже
    /// записанную чужую строку. Пока клиент отправлял файлы по одному подряд, этого было не видно;
    /// с параллельной отправкой на это натыкается первая же загрузка альбома.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Files_of_one_album_uploaded_at_once_settle_on_a_single_artist()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = Unique("Race");

        var files = Enumerable.Range(1, 6)
            .Select(number => SyntheticMp3.Tagged(
                $"{name}-{number}.mp3", $"{name} {number}", $"{name} Artist", $"{name} Album",
                $"{name} Genre", null, number))
            .ToList();

        // Каждый файл — своим запросом, все разом: ровно то, что делает страница загрузки.
        var results = await Task.WhenAll(files.Select(file => UploadAsync(client, [file])));

        Assert.All(results, result => Assert.Empty(result.Failed));
        Assert.Equal(files.Count, results.Sum(result => result.Uploaded.Count));

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Artist"), Cancel.Token));

        Assert.Equal(1, await db.Albums.CountAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Album"), Cancel.Token));

        Assert.Equal(1, await db.Genres.CountAsync(
            g => g.NormalizedName == Normalize.Key($"{name} Genre"), Cancel.Token));

        // И все шесть треков сведены в один альбом, а не расползлись по одинаково названным.
        Assert.Single(results.SelectMany(result => result.Uploaded).Select(t => t.AlbumId).Distinct());
    }

    // ── Вспомогательное ─────────────────────────────────────────────────────────────────────

    /// <summary>Имя, уникальное для прогона: набор делит одну базу на все тесты коллекции.</summary>
    private static string Unique(string prefix) => $"{prefix} {Guid.CreateVersion7():N}"[..24];

    private static async Task<UploadResultDto> UploadAsync(
        HttpClient client, IReadOnlyList<UploadFile> files)
    {
        using var form = new MultipartFormDataContent();

        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Content);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(content, "files", file.FileName);
        }

        var response = await client.PostAsync("/api/tracks/upload", form, Cancel.Token);

        // Пакет, в котором не уцелело ничего, отвечает 400 и тем же телом — оно тоже разбирается.
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"unexpected status {response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel.Token)}");

        return (await response.Content.ReadFromJsonAsync<UploadResultDto>(
            Json, Cancel.Token))!;
    }

    private record UploadFile(string FileName, string ContentType, byte[] Content);

    /// <summary>
    /// Синтетический MP3: несколько настоящих кадров MPEG-1 Layer III и теги, записанные той же
    /// библиотекой, которой их потом читает приложение. Держать в репозитории бинарный образец ради
    /// этого не пришлось бы кстати — а от подделки тегов вручную тест проверял бы разбор ID3, а не
    /// загрузку.
    /// </summary>
    private static class SyntheticMp3
    {
        /// <summary>Заголовок кадра: MPEG-1 Layer III, 128 кбит/с, 44100 Гц, стерео, без CRC.</summary>
        private static readonly byte[] FrameHeader = [0xFF, 0xFB, 0x90, 0x00];

        private const int FrameLength = 417;
        private const int FrameCount = 120;

        public static UploadFile Tagged(
            string fileName,
            string title,
            string artist,
            string? album,
            string? genre,
            int? year,
            int? track,
            string? albumArtist = null,
            byte[]? cover = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"caimack-upload-{Guid.CreateVersion7():N}.mp3");

            try
            {
                System.IO.File.WriteAllBytes(path, Silence());

                using (var tagged = TagLib.File.Create(path, "audio/mpeg", TagLib.ReadStyle.Average))
                {
                    tagged.Tag.Title = title;
                    tagged.Tag.Performers = [artist];

                    if (album is not null)
                        tagged.Tag.Album = album;

                    if (albumArtist is not null)
                        tagged.Tag.AlbumArtists = [albumArtist];

                    if (genre is not null)
                        tagged.Tag.Genres = [genre];

                    if (year is { } value)
                        tagged.Tag.Year = (uint)value;

                    if (track is { } number)
                        tagged.Tag.Track = (uint)number;

                    // Содержимое неважно: путь загрузки лишь проверяет, что байты есть, а разбирать
                    // их будет подменённый обработчик, который всё равно откажет.
                    if (cover is not null)
                    {
                        tagged.Tag.Pictures = [
                            new TagLib.Picture(new TagLib.ByteVector(cover))
                            {
                                Type = TagLib.PictureType.FrontCover,
                                MimeType = "image/png",
                            },
                        ];
                    }

                    tagged.Save();
                }

                return new UploadFile(fileName, "audio/mpeg", System.IO.File.ReadAllBytes(path));
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }

        /// <summary>Кадры с нулевыми данными — TagLib нужна их длина, а не содержимое.</summary>
        private static byte[] Silence()
        {
            var audio = new byte[FrameLength * FrameCount];

            for (var frame = 0; frame < FrameCount; frame++)
                FrameHeader.CopyTo(audio, frame * FrameLength);

            return audio;
        }
    }
}
