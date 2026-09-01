// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using Xunit;

namespace MusicStreaming.IntegrationTests;

// Фикстура и база здесь общие на всю коллекцию, поэтому эти тесты намеренно ничего не засевают
// и ничего не меняют: соседи рядом проверяют, что похожесть не пересобирается над нетронутой
// библиотекой и что планировщик не уходит в Seq Scan, — а и то и другое зависит от объёма данных.
[Collection(nameof(RecommendationApiCollection))]
public class JsonETagTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task A_repeated_json_request_is_answered_with_304_and_an_empty_body()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        var first = await client.GetAsync("/api/genres", Cancel.Token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        Assert.True((await first.Content.ReadAsByteArrayAsync(Cancel.Token)).Length > 0);

        using var repeat = new HttpRequestMessage(HttpMethod.Get, "/api/genres");
        repeat.Headers.IfNoneMatch.Add(etag);

        var second = await client.SendAsync(repeat, Cancel.Token);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync(Cancel.Token));
    }

    [Fact]
    public async Task An_etag_follows_the_body_rather_than_the_address()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        var genres = await client.GetAsync("/api/genres", Cancel.Token);
        var playlists = await client.GetAsync("/api/playlists", Cancel.Token);

        Assert.NotNull(genres.Headers.ETag);
        Assert.NotNull(playlists.Headers.ETag);
        Assert.NotEqual(genres.Headers.ETag, playlists.Headers.ETag);

        // Тот же ресурс — тот же ETag, иначе 304 не сработал бы никогда.
        var again = await client.GetAsync("/api/genres", Cancel.Token);
        Assert.Equal(genres.Headers.ETag, again.Headers.ETag);
    }
}
