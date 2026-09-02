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


    /// <summary>
    /// Подсказка ms_session живёт столько же, сколько refresh-токен, и переживает его отзыв.
    /// Пока отказ не уносил её с собой, слушатель с мёртвой сессией оказывался заперт: клиент
    /// ловил 401, уходил на /login, middleware видел подсказку и заворачивал его обратно на
    /// страницу, где всё снова отвечало 401. Разрывалось только режимом инкогнито.
    /// </summary>
    [Fact]
    public async Task A_rejected_refresh_takes_the_session_cookies_with_it()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();

        var response = await SendRefreshAsync(client, "not-a-token-at-all");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var cleared = ClearedCookies(response);

        Assert.Contains(RefreshCookie, cleared);
        Assert.Contains("ms_access", cleared);
        Assert.Contains("ms_session", cleared);
    }

    /// <summary>
    /// Refresh-кука обязана быть видна на путях страниц.
    ///
    /// Пока она жила на /api/auth, браузер не присылал её middleware фронтенда — тот работает
    /// на навигациях и /api исключает из матчера, — и `sessionGate` считал вошедшего гостем:
    /// вход проходил, а его тут же разворачивало обратно на /login. Проверять это в
    /// sessionGate.test.ts нечем — там чистая функция, которой путь куки не виден.
    /// </summary>
    [Fact]
    public async Task The_refresh_cookie_is_visible_to_page_navigations()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();
        await SignInAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { username = Username, password = Password }, Cancel.Token);
        response.EnsureSuccessStatusCode();

        var issued = response.Headers.GetValues("Set-Cookie")
            .Single(cookie =>
                cookie.StartsWith($"{RefreshCookie}=", StringComparison.Ordinal)
                && cookie[(RefreshCookie.Length + 1)..].Split(';')[0].Length > 0);

        Assert.Contains("path=/;", issued, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path=/api/auth", issued, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Имена кук, которые ответ гасит: пустое значение и срок в прошлом.</summary>
    private static HashSet<string> ClearedCookies(HttpResponseMessage response)
    {
        var cleared = new HashSet<string>(StringComparer.Ordinal);

        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return cleared;

        foreach (var cookie in cookies)
        {
            var separator = cookie.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;

            var name = cookie[..separator];
            var value = cookie[(separator + 1)..].Split(';')[0];

            if (value.Length == 0) cleared.Add(name);
        }

        return cleared;
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
            if (!cookie.StartsWith($"{RefreshCookie}=", StringComparison.Ordinal)) continue;

            // Пустое значение — это гашение старой копии куки, а не выданный токен.
            var value = cookie[(RefreshCookie.Length + 1)..].Split(';')[0];
            if (value.Length > 0) return value;
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
