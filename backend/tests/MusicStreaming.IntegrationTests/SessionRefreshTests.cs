// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace MusicStreaming.IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class SessionRefreshTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_expired_access_token_is_renewed_from_the_refresh_cookie()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var session = await SignInInThePastAsync("refresh-probe", "refresh-probe-password");

        var expired = await session.Client.GetAsync("/api/auth/me", Cancel.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        var refreshed = await session.Client.PostAsync("/api/auth/refresh", null, Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var retried = await session.Client.GetAsync("/api/auth/me", Cancel.Token);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
    }

    [Fact]
    public async Task Two_renewals_of_the_same_token_do_not_end_the_session()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var session = await SignInInThePastAsync("refresh-race", "refresh-race-password");

        // Браузер и middleware Next живут в разных процессах и обновляют сессию независимо,
        // предъявляя одну и ту же куку. Общего «одновременно» у них нет.
        var results = await Task.WhenAll(
            SendRefreshAsync(session.Cookie), SendRefreshAsync(session.Cookie));

        Assert.All(results, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        // Сессия обязана пережить гонку: кука победившего запроса продолжает работать.
        var survivor = await SendRefreshAsync(CookieFrom(results[^1]));
        Assert.Equal(HttpStatusCode.OK, survivor.StatusCode);
    }

    [Fact]
    public async Task A_token_replayed_after_the_grace_window_ends_the_session()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var session = await SignInInThePastAsync("refresh-replay", "refresh-replay-password");

        Assert.Equal(HttpStatusCode.OK, (await SendRefreshAsync(session.Cookie)).StatusCode);

        // Та же кука спустя минуту: окно снисхождения к гонке — двадцать секунд.
        using (fixture.Clock.PinnedAt(DateTimeOffset.UtcNow.AddMinutes(1)))
        {
            var replayed = await SendRefreshAsync(session.Cookie);
            Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
        }
    }

    private sealed record Session(HttpClient Client, string Cookie);

    private static string CookieFrom(HttpResponseMessage response) =>
        string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie")
                .Select(raw => raw.Split(';')[0])
                .Where(pair => pair.Contains('=') && !pair.EndsWith('=')));

    private async Task<HttpResponseMessage> SendRefreshAsync(string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", cookie);

        return await fixture.CreateAnonymousClient().SendAsync(request, Cancel.Token);
    }

    /// <summary>
    /// Сессия с живой refresh-кукой и мёртвым access-токеном.
    ///
    /// Вход выполняется «час назад»: access живёт десять минут, поэтому к настоящему моменту он
    /// уже протух по-настоящему, а refresh (тридцать дней) ещё жив. Состояние получается за
    /// миллисекунды и без ожидания.
    /// </summary>
    private async Task<Session> SignInInThePastAsync(string username, string password)
    {
        var owner = await fixture.CreateSignedInClientAsync();
        var created = await owner.PostAsJsonAsync(
            "/api/admin/users",
            new { username, password, displayName = username, isAdmin = false },
            Cancel.Token);

        Assert.True(
            created.IsSuccessStatusCode || created.StatusCode == HttpStatusCode.Conflict,
            await created.Content.ReadAsStringAsync(Cancel.Token));

        var client = fixture.CreateAnonymousClient();

        using (fixture.Clock.PinnedAt(DateTimeOffset.UtcNow.AddHours(-1)))
        {
            var login = await client.PostAsJsonAsync(
                "/api/auth/login", new { username, password }, Cancel.Token);

            login.EnsureSuccessStatusCode();

            return new Session(client, CookieFrom(login));
        }
    }
}
