// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Integrations;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

public class LastfmTagProviderTests
{
    [Fact]
    public async Task Counts_become_weights_and_collection_tags_are_dropped()
    {
        var provider = Provider("""
            {"toptags":{"tag":[
                {"name":"Shoegaze","count":100},
                {"name":"seen live","count":98},
                {"name":"dream pop","count":"64"},
                {"name":"male vocalists","count":40},
                {"name":"obscure","count":2}
            ]}}
            """);

        var tags = await provider.ArtistTagsAsync("Slowdive", TestContext.Current.CancellationToken);

        Assert.Equal(["shoegaze", "dream pop"], tags.Select(tag => tag.Name));
        Assert.Equal(1.0, tags[0].Weight, 3);
        Assert.Equal(0.64, tags[1].Weight, 3);
    }

    [Fact]
    public async Task An_error_body_reads_as_no_tags_rather_than_a_failure()
    {
        var provider = Provider("""{"error":6,"message":"The artist you supplied could not be found"}""");

        Assert.Empty(await provider.TrackTagsAsync("Nobody", "Nothing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unreachable_provider_reads_as_no_tags()
    {
        var provider = Provider(_ => throw new HttpRequestException("down"));

        Assert.Empty(await provider.ArtistTagsAsync("Slowdive", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_requested()
    {
        var provider = Provider(
            _ => throw new InvalidOperationException("the provider must not call out"),
            apiKey: string.Empty);

        Assert.False(provider.IsConfigured);
        Assert.Empty(await provider.ArtistTagsAsync("Slowdive", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task No_more_tags_are_kept_than_the_options_allow()
    {
        var many = string.Join(
            ',',
            Enumerable.Range(0, 30).Select(index => $"{{\"name\":\"tag {index}\",\"count\":100}}"));

        var provider = Provider($"{{\"toptags\":{{\"tag\":[{many}]}}}}", maxTags: 5);

        Assert.Equal(5, (await provider.ArtistTagsAsync("Any", TestContext.Current.CancellationToken)).Count);
    }

    private static LastfmTagProvider Provider(string body, int maxTags = 12, string apiKey = "key") =>
        Provider(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            },
            maxTags,
            apiKey);

    private static LastfmTagProvider Provider(
        Func<HttpRequestMessage, HttpResponseMessage> respond, int maxTags = 12, string apiKey = "key") =>
        new(
            new HttpClient(new StubHandler(respond)),
            Options.Create(new LastfmOptions { ApiKey = apiKey, ApiSecret = "secret" }),
            Options.Create(new TagEnrichmentOptions { MaxTagsPerEntity = maxTags }),
            NullLogger<LastfmTagProvider>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
