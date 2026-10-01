// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class LoginTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_unknown_username_is_refused_like_a_wrong_password()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = fixture.CreateClient();

        var unknown = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = $"nobody-{Guid.CreateVersion7():N}", password = "whatever-password" },
            Cancel.Token);

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = RecommendationApiFixture.OwnerUsername, password = "not-the-owner-password" },
            Cancel.Token);

        Assert.Equal(HttpStatusCode.Forbidden, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(Cancel.Token),
            await unknown.Content.ReadAsStringAsync(Cancel.Token));
    }
}
