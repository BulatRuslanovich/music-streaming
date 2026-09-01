// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

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
        var name = TrackUploadTestClient.UniqueName("Solo");

        var result = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}.mp3", title: $"{name} Title", artist: $"{name} Artist",
                album: $"{name} Album", genre: $"{name} Genre", year: 1999, track: 7),
        ], Json);

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

    [Fact]
    public async Task A_track_without_a_year_of_its_own_does_not_borrow_the_albums()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Undated");

        var result = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-a.mp3", $"{name} Dated", $"{name} Artist", $"{name} Album",
                null, year: 1994, track: 1),
            SyntheticMp3.Tagged($"{name}-b.mp3", $"{name} Undated", $"{name} Artist", $"{name} Album",
                null, year: null, track: 2),
        ], Json);

        Assert.Empty(result.Failed);
        Assert.Equal(1994, result.Uploaded.Single(t => t.Title == $"{name} Dated").Year);
        Assert.Null(result.Uploaded.Single(t => t.Title == $"{name} Undated").Year);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var album = await db.Albums.SingleAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Album"), Cancel.Token);

        Assert.Equal(1994, album.Year);
    }

    [Fact]
    public async Task A_raw_uploaded_file_is_streamed_into_the_library_without_multipart_buffering()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Streamed");
        var file = SyntheticMp3.Tagged(
            $"{name}.mp3", $"{name} Title", $"{name} Artist", $"{name} Album", null, null, 1);

        var result = await TrackUploadTestClient.UploadOneAsync(client, file, Json);

        Assert.Empty(result.Failed);
        var uploaded = Assert.Single(result.Uploaded);
        Assert.Equal($"{name} Title", uploaded.Title);
        Assert.Equal($"{name} Artist", uploaded.ArtistName);
    }

    [Fact]
    public async Task A_new_track_is_enriched_after_the_upload_response_returns()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        using var factory = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LibraryEnrichment:Enabled", "true");
            builder.UseSetting("AudioDb:RequestDelayMs", "0");
            builder.UseSetting("Lrclib:RequestDelayMs", "0");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IArtistImageProvider>();
                services.RemoveAll<ILyricsProvider>();
                services.AddSingleton<BlockingArtistImageProvider>();
                services.AddSingleton<IArtistImageProvider>(provider =>
                    provider.GetRequiredService<BlockingArtistImageProvider>());
                services.AddSingleton<RecordingLyricsProvider>();
                services.AddSingleton<ILyricsProvider>(provider =>
                    provider.GetRequiredService<RecordingLyricsProvider>());
            });
        });

        var client = await SignInAsync(factory);
        var name = TrackUploadTestClient.UniqueName("Enriched");
        var file = SyntheticMp3.Tagged(
            $"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, null, 1);

        var result = await TrackUploadTestClient.UploadOneAsync(client, file, Json)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var uploaded = Assert.Single(result.Uploaded);

        var images = factory.Services.GetRequiredService<BlockingArtistImageProvider>();
        await images.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal($"{name} Artist", images.ArtistName);

        images.Release.TrySetResult();

        var lyrics = factory.Services.GetRequiredService<RecordingLyricsProvider>();
        await lyrics.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal($"{name} Title", lyrics.Query?.Title);

        await EventuallyAsync(async () =>
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasLyrics = await db.TrackLyrics.AnyAsync(item => item.TrackId == uploaded.Id, Cancel.Token);
            var hasArtistImage = await db.Artists
                .Where(artist => artist.Id == uploaded.ArtistId)
                .AnyAsync(artist => artist.ImagePath != null, Cancel.Token);
            return hasLyrics && hasArtistImage;
        });
    }

    [Fact]
    public async Task A_batch_from_one_album_creates_its_artist_album_and_genre_exactly_once()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Batch");

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

        var result = await TrackUploadTestClient.UploadAsync(client, files, Json);

        Assert.Empty(result.Failed);
        Assert.Equal(5, result.Uploaded.Count);

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

    [Fact]
    public async Task An_artist_from_an_earlier_upload_is_reused_by_the_next_one()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Across");

        var first = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-a.mp3", $"{name} A", $"{name} Artist", $"{name} Album", null, null, 1),
        ], Json);

        var second = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-b.mp3", $"{name} B", $"{name} Artist", $"{name} Album", null, null, 2),
        ], Json);

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

    [Fact]
    public async Task An_artist_named_both_on_the_track_and_on_the_album_is_created_once()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Both");

        var result = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}.mp3", $"{name} Title", $"{name} Artist", $"{name} Album",
                null, null, 1, albumArtist: $"{name} Artist"),
        ], Json);

        Assert.Empty(result.Failed);
        Assert.Single(result.Uploaded);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Artist"), Cancel.Token));
    }

    [Fact]
    public async Task A_rejected_file_does_not_take_the_rest_of_the_batch_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Mixed");

        var result = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-good.mp3", $"{name} Good", $"{name} Artist", $"{name} Album", null, null, 1),
            new TestUploadFile($"{name}-bad.mp3", "audio/mpeg", [0x00, 0x01, 0x02, 0x03]),
            SyntheticMp3.Tagged($"{name}-also.mp3", $"{name} Also", $"{name} Artist", $"{name} Album", null, null, 2),
        ], Json);

        Assert.Equal(2, result.Uploaded.Count);
        Assert.Single(result.Failed);
        Assert.Equal($"{name}-bad.mp3", result.Failed[0].FileName);

        Assert.Single(result.Uploaded.Select(t => t.AlbumId).Distinct());

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Albums.CountAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Album"), Cancel.Token));
    }

    [Fact]
    public async Task A_file_that_fails_after_creating_its_tags_leaves_nothing_behind()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var name = TrackUploadTestClient.UniqueName("Doomed");

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

        var result = await TrackUploadTestClient.UploadAsync(client, [
            SyntheticMp3.Tagged($"{name}-bad.mp3", $"{name} Bad", $"{name} Doomed Artist",
                $"{name} Doomed Album", null, null, 1, cover: [1, 2, 3, 4]),
            SyntheticMp3.Tagged($"{name}-ok.mp3", $"{name} Ok", $"{name} Fine Artist",
                $"{name} Fine Album", null, null, 2),
        ], Json);

        Assert.Single(result.Failed);
        Assert.Equal($"{name}-bad.mp3", result.Failed[0].FileName);
        Assert.Single(result.Uploaded);
        Assert.Equal($"{name} Ok", result.Uploaded[0].Title);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Fine Artist"), Cancel.Token));

        Assert.Equal(0, await db.Artists.CountAsync(
            a => a.NormalizedName == Normalize.Key($"{name} Doomed Artist"), Cancel.Token));

        Assert.Equal(0, await db.Albums.CountAsync(
            a => a.NormalizedTitle == Normalize.Key($"{name} Doomed Album"), Cancel.Token));

        Assert.Equal(0, await db.Tracks.CountAsync(
            t => t.Title == $"{name} Bad", Cancel.Token));
    }

    private sealed class ExplodingImageProcessor : IImageProcessor
    {
        public Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
            Stream source, IReadOnlyList<int> edges, CancellationToken ct = default) =>
            throw new InvalidOperationException("image processing is unavailable");
    }

    [Fact]
    public async Task The_same_file_twice_is_refused_as_a_duplicate()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Twice");
        var file = SyntheticMp3.Tagged($"{name}.mp3", $"{name} Title", $"{name} Artist", null, null, null, 1);

        var first = await TrackUploadTestClient.UploadAsync(client, [file], Json);
        var second = await TrackUploadTestClient.UploadAsync(client, [file], Json);

        Assert.Single(first.Uploaded);
        Assert.Empty(second.Uploaded);
        Assert.Single(second.Failed);
    }

    [Fact]
    public async Task Files_of_one_album_uploaded_at_once_settle_on_a_single_artist()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var name = TrackUploadTestClient.UniqueName("Race");

        var files = Enumerable.Range(1, 6)
            .Select(number => SyntheticMp3.Tagged(
                $"{name}-{number}.mp3", $"{name} {number}", $"{name} Artist", $"{name} Album",
                $"{name} Genre", null, number))
            .ToList();

        var results = await Task.WhenAll(files.Select(file =>
            TrackUploadTestClient.UploadAsync(client, [file], Json)));

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

        Assert.Single(results.SelectMany(result => result.Uploaded).Select(t => t.AlbumId).Distinct());
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                username = RecommendationApiFixture.OwnerUsername,
                password = RecommendationApiFixture.OwnerPassword,
            },
            Cancel.Token);

        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(50, Cancel.Token);
        }

        Assert.True(await condition(), "the background enrichment did not persist its result");
    }

    private sealed class BlockingArtistImageProvider : IArtistImageProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? ArtistName { get; private set; }

        public async Task<ArtistImageLookupResult> LookupAsync(string artistName, CancellationToken ct)
        {
            ArtistName = artistName;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new ArtistImageLookupResult(ArtistImageLookupStatus.Found, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        }
    }

    private sealed class RecordingLyricsProvider : ILyricsProvider
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LyricsQuery? Query { get; private set; }

        public Task<LyricsLookupResult> LookupAsync(LyricsQuery query, CancellationToken ct)
        {
            Query = query;
            Called.TrySetResult();
            return Task.FromResult(new LyricsLookupResult(
                LyricsLookupStatus.Found,
                "[00:01.00]Found after upload",
                Synced: true));
        }
    }

    private static class SyntheticMp3
    {
        private static readonly byte[] FrameHeader = [0xFF, 0xFB, 0x90, 0x00];

        private const int FrameLength = 417;
        private const int FrameCount = 120;

        public static TestUploadFile Tagged(
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
                File.WriteAllBytes(path, Silence());

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

                return new TestUploadFile(fileName, "audio/mpeg", File.ReadAllBytes(path));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static byte[] Silence()
        {
            var audio = new byte[FrameLength * FrameCount];

            for (var frame = 0; frame < FrameCount; frame++)
                FrameHeader.CopyTo(audio, frame * FrameLength);

            return audio;
        }
    }
}
