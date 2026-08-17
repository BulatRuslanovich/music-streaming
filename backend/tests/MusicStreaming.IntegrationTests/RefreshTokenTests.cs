using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Ротация refresh-токенов.
///
/// <para>
/// Каждое продление отзывает свой токен и выдаёт новый, поэтому предъявленный дважды означает
/// либо кражу, либо две вкладки, пошедшие продлеваться одновременно. Отличаются они только тем,
/// сколько прошло с отзыва, а цена ошибки высока в обе стороны: спутать кражу с гонкой значит
/// оставить украденную сессию живой, спутать гонку с кражей — выкинуть человека из приложения на
/// ровном месте. Поэтому проверяются оба случая.
/// </para>
///
/// <para>
/// Куки здесь ведутся руками, а не контейнером клиента: весь смысл проверок в том, чтобы
/// предъявить <em>устаревший</em> токен, а контейнер аккуратно заменяет его на свежий — то есть
/// делает ровно то, что нужно исключить.
/// </para>
/// </summary>
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

        // Свежий токен работает — сессия та же самая, продлевается дальше.
        Assert.NotNull(await RefreshAsync(client, second));
    }

    /// <summary>
    /// Две вкладки, упёршиеся в 401 одновременно, обе уходят продлеваться со старой кукой. Вторая
    /// приходит с токеном, который первая только что отозвала, — и это не повод закрывать сессию:
    /// внутри короткого окна такой токен ещё раз проворачивается как обычный.
    /// </summary>
    [Fact]
    public async Task A_second_refresh_with_the_same_token_moments_later_is_a_race_not_a_theft()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();
        var shared = await SignInAsync(client);

        var fromFirstTab = await RefreshAsync(client, shared);
        Assert.NotNull(fromFirstTab);

        // Вторая вкладка успела отправить свой запрос до того, как увидела новую куку.
        var fromSecondTab = await RefreshAsync(client, shared);
        Assert.NotNull(fromSecondTab);

        // Обе продолжают работать: гонка не закрыла ничего.
        Assert.NotNull(await RefreshAsync(client, fromFirstTab));
        Assert.NotNull(await RefreshAsync(client, fromSecondTab));
    }

    /// <summary>
    /// Тот же токен, предъявленный позже окна, — признак того, что цепочку продолжает кто-то ещё.
    /// Отличить настоящего клиента от чужого нечем, поэтому закрываются оба.
    /// </summary>
    [Fact]
    public async Task Reusing_a_long_revoked_token_revokes_every_session_of_that_user()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = Raw();
        var stolen = await SignInAsync(client);

        var mine = await RefreshAsync(client, stolen);
        Assert.NotNull(mine);

        // Ждать окно по-настоящему значило бы держать набор двадцать секунд ради одной проверки.
        await AgeRevocationsAsync();

        var replayed = await SendRefreshAsync(client, stolen);
        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);

        // Действующая сессия закрыта вместе с украденной: какая из них чужая, отсюда не видно.
        var afterwards = await SendRefreshAsync(client, mine);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);

        // Пароль по-прежнему пускает — закрыты сессии, а не учётная запись.
        Assert.NotNull(await SignInAsync(Raw()));
    }

    // ── Обвязка ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Клиент без ведения кук: каждый запрос несёт ровно тот токен, который ему дали.</summary>
    private HttpClient Raw() => fixture.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>
    /// Заводит подопытную учётную запись (если её ещё нет), входит и возвращает выданный
    /// refresh-токен. Своя запись на весь класс, а не общий клиент набора: проверки здесь
    /// отзывают все сессии пользователя.
    /// </summary>
    private async Task<string> SignInAsync(HttpClient client)
    {
        var admin = await fixture.CreateSignedInClientAsync();

        var created = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new { username = Username, password = Password, displayName = Username, isAdmin = false },
            Cancel.Token);

        // Conflict означает, что запись завёл предыдущий тест этого класса — это и нужно.
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

    /// <summary>Продлевает сессию и возвращает выданный токен; <c>null</c>, если запрос отвергнут.</summary>
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

    /// <summary>Сдвигает отзывы в прошлое, чтобы окно на одновременное продление уже истекло.</summary>
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
