// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class ClientConfigTests(RecommendationApiFixture fixture)
{
    private static readonly string[] Expected =
    [
        "historyThresholdSeconds",
        "maxUploadBytes",
        "maxImageUploadBytes",
        "audioQualities",
        "hlsEnabled",
        "accessTokenMinutes",
    ];

    [Fact]
    public async Task The_client_configuration_carries_every_field_the_player_reads()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        var config = await client.GetFromJsonAsync<JsonElement>(
            "/api/config", RecommendationApiFixture.Json, Cancel.Token);

        var actual = config.EnumerateObject().Select(property => property.Name).ToHashSet();

        foreach (var field in Expected)
            Assert.Contains(field, actual);
    }

    [Fact]
    public async Task The_access_token_lifetime_is_published_so_the_client_can_renew_ahead_of_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();

        var config = await client.GetFromJsonAsync<JsonElement>(
            "/api/config", RecommendationApiFixture.Json, Cancel.Token);

        Assert.True(config.GetProperty("accessTokenMinutes").GetInt32() > 0);
    }
}
