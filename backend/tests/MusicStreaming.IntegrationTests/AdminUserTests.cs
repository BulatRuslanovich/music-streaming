// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Infrastructure.Persistence;
using Xunit;

namespace MusicStreaming.IntegrationTests;


[Collection(nameof(RecommendationApiCollection))]
public class AdminUserTests(RecommendationApiFixture fixture)
{
    [Fact]
    public async Task An_administrator_cannot_deactivate_themselves()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var owner = await OwnerAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{owner}/active", new SetUserActiveRequest(false), Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await IsActiveAsync(owner));
    }

    [Fact]
    public async Task An_administrator_cannot_revoke_their_own_rights()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var client = await fixture.CreateSignedInClientAsync();
        var owner = await OwnerAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/admin/users/{owner}/role", new SetUserRoleRequest(false), Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_last_active_administrator_is_protected_even_from_a_stale_token()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var owner = await fixture.CreateSignedInClientAsync();
        var ownerId = await OwnerAsync();
        var second = await CreateAsync(owner, isAdmin: true);

        var theirs = fixture.CreateAnonymousClient();
        (await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = second.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.Where(u => u.Id == second.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(user => user.IsAdmin, false), Cancel.Token);
        }

        var demote = await theirs.PutAsJsonAsync(
            $"/api/admin/users/{ownerId}/role", new SetUserRoleRequest(false), Cancel.Token);

        var deactivate = await theirs.PutAsJsonAsync(
            $"/api/admin/users/{ownerId}/active", new SetUserActiveRequest(false), Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deactivate.StatusCode);

        using var check = fixture.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await context.Users.CountAsync(u => u.IsAdmin && u.IsActive, Cancel.Token));
    }

    [Fact]
    public async Task Deactivation_ends_every_session_and_blocks_signing_in()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var theirs = fixture.CreateAnonymousClient();
        var signIn = await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token);

        signIn.EnsureSuccessStatusCode();

        var deactivated = await admin.PutAsJsonAsync(
            $"/api/admin/users/{user.Id}/active", new SetUserActiveRequest(false), Cancel.Token);

        deactivated.EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await theirs.PostAsync("/api/auth/refresh", null, Cancel.Token)).StatusCode);

        var again = fixture.CreateAnonymousClient();
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await again.PostAsJsonAsync(
                "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token)).StatusCode);
    }

    [Fact]
    public async Task Reactivation_lets_the_account_back_in()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        await admin.PutAsJsonAsync($"/api/admin/users/{user.Id}/active", new SetUserActiveRequest(false), Cancel.Token);
        var restored = await admin.PutAsJsonAsync(
            $"/api/admin/users/{user.Id}/active", new SetUserActiveRequest(true), Cancel.Token);

        restored.EnsureSuccessStatusCode();

        var client = fixture.CreateAnonymousClient();
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token);

        signIn.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Resetting_a_password_replaces_it_and_ends_the_old_sessions()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var theirs = fixture.CreateAnonymousClient();
        (await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();

        const string replacement = "replacement-password";
        (await admin.PostAsJsonAsync(
            $"/api/admin/users/{user.Id}/password", new ResetPasswordRequest(replacement), Cancel.Token))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await theirs.PostAsync("/api/auth/refresh", null, Cancel.Token)).StatusCode);

        var fresh = fixture.CreateAnonymousClient();
        (await fresh.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = replacement }, Cancel.Token))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_reset_password_still_has_to_be_a_usable_password()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var response = await admin.PostAsJsonAsync(
            $"/api/admin/users/{user.Id}/password", new ResetPasswordRequest("short"), Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revoking_sessions_leaves_the_password_alone()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var theirs = fixture.CreateAnonymousClient();
        (await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();

        (await admin.PostAsync($"/api/admin/users/{user.Id}/sessions/revoke", null, Cancel.Token))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await theirs.PostAsync("/api/auth/refresh", null, Cancel.Token)).StatusCode);

        var again = fixture.CreateAnonymousClient();
        (await again.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Changing_your_own_password_keeps_you_signed_in()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var theirs = fixture.CreateAnonymousClient();
        (await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();

        const string replacement = "self-chosen-password";
        (await theirs.PostAsJsonAsync(
            "/api/me/password", new ChangePasswordRequest(Password, replacement), Cancel.Token))
            .EnsureSuccessStatusCode();

        (await theirs.GetAsync("/api/auth/me", Cancel.Token)).EnsureSuccessStatusCode();
        (await theirs.PostAsync("/api/auth/refresh", null, Cancel.Token)).EnsureSuccessStatusCode();

        var fresh = fixture.CreateAnonymousClient();
        (await fresh.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = replacement }, Cancel.Token))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Changing_a_password_requires_knowing_the_current_one()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var theirs = fixture.CreateAnonymousClient();
        (await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();

        var wrong = await theirs.PostAsJsonAsync(
            "/api/me/password", new ChangePasswordRequest("not-the-password", "another-password"), Cancel.Token);

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        var unchanged = await theirs.PostAsJsonAsync(
            "/api/me/password", new ChangePasswordRequest(Password, Password), Cancel.Token);

        Assert.Equal(HttpStatusCode.BadRequest, unchanged.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_listener_cannot_manage_accounts()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var admin = await fixture.CreateSignedInClientAsync();
        var user = await CreateAsync(admin, isAdmin: false);

        var theirs = fixture.CreateAnonymousClient();
        (await theirs.PostAsJsonAsync(
            "/api/auth/login", new { username = user.Username, password = Password }, Cancel.Token))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await theirs.GetAsync("/api/admin/users", Cancel.Token)).StatusCode);

        var attempt = await theirs.PutAsJsonAsync(
            $"/api/admin/users/{user.Id}/role", new SetUserRoleRequest(true), Cancel.Token);

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }

    private const string Password = "integration-user-password";

    private static int _counter;

    private static async Task<UserDto> CreateAsync(HttpClient admin, bool isAdmin)
    {
        var username = $"managed{Interlocked.Increment(ref _counter)}-{Guid.CreateVersion7():N}"[..20];

        var response = await admin.PostAsJsonAsync("/api/admin/users", new
        {
            username,
            password = Password,
            displayName = username,
            isAdmin,
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private async Task<Guid> OwnerAsync()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users
            .Where(u => u.Username == RecommendationApiFixture.OwnerUsername)
            .Select(u => u.Id)
            .SingleAsync();
    }

    private async Task<bool> IsActiveAsync(Guid userId)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Users.Where(u => u.Id == userId).Select(u => u.IsActive).SingleAsync();
    }
}
