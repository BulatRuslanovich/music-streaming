// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class RefreshTokenTests(RecommendationApiFixture fixture)
{
    private const string Username = "rotation";
    private const string Password = "integration-password-rotation";
    private const string RefreshCookie = "ms_refresh";

    [Fact]
    public async Task Refreshing_rotates_the_token()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();
        var first = await SignInAsync(client);

        var second = await RefreshAsync(client, first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);

        Assert.NotNull(await RefreshAsync(client, second));
    }

    [Fact]
    public async Task A_second_refresh_with_the_same_token_moments_later_is_a_race_not_a_theft()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();
        var shared = await SignInAsync(client);

        var fromFirstTab = await RefreshAsync(client, shared);
        Assert.NotNull(fromFirstTab);

        var fromSecondTab = await RefreshAsync(client, shared);
        Assert.NotNull(fromSecondTab);

        Assert.NotNull(await RefreshAsync(client, fromFirstTab));
        Assert.NotNull(await RefreshAsync(client, fromSecondTab));
    }

    [Fact]
    public async Task Reusing_a_long_revoked_token_revokes_every_session_of_that_user()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();
        var stolen = await SignInAsync(client);

        var mine = await RefreshAsync(client, stolen);
        Assert.NotNull(mine);

        await AgeRevocationsAsync();

        var replayed = await SendRefreshAsync(client, stolen);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        var afterwards = await SendRefreshAsync(client, mine);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);

        Assert.NotNull(await SignInAsync(Raw()));
    }


    private HttpClient Raw() => fixture.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private async Task<string> SignInAsync(HttpClient client)
    {
        var admin = await fixture.CreateSignedInClientAsync();

        var created = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new { username = Username, password = Password, displayName = Username, isAdmin = false },
            Cancel.Token);

        if (created.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.Conflict)
            created.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { username = Username, password = Password }, Cancel.Token);

        response.EnsureSuccessStatusCode();

        return TokenOf(response) ?? throw new InvalidOperationException("Login issued no refresh token.");
    }

    private Task<HttpResponseMessage> SendRefreshAsync(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");

        return client.SendAsync(request, Cancel.Token);
    }

    private async Task<string?> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await SendRefreshAsync(client, refreshToken);
        response.EnsureSuccessStatusCode();

        return TokenOf(response);
    }

    private static string? TokenOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith($"{RefreshCookie}=", StringComparison.Ordinal))
                return cookie[(RefreshCookie.Length + 1)..].Split(';')[0];
        }

        return null;
    }

    private async Task AgeRevocationsAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var longAgo = DateTimeOffset.UtcNow.AddMinutes(-5);

        await db.RefreshTokens
            .Where(t => t.RevokedAt != null && t.RevokedAt > longAgo)
            .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, longAgo), Cancel.Token);
    }
}
