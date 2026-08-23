// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Api.Auth;

public static class AuthCookies
{
    public const string AccessTokenCookie = "ms_access";
    public const string RefreshTokenCookie = "ms_refresh";
    private const string RefreshCookiePath = "/api/auth";

    public static bool RequireSecure(HttpRequest request, IWebHostEnvironment environment) =>
        !environment.IsDevelopment() || request.IsHttps;

    public static void Write(HttpResponse response, AuthResultDto auth, bool requireSecure)
    {
        var expires = auth.RefreshTokenExpiresAt;

        response.Cookies.Append(
            AccessTokenCookie, auth.AccessToken, OptionsFor("/", requireSecure, expires));

        response.Cookies.Append(
            RefreshTokenCookie, auth.RefreshToken, OptionsFor(RefreshCookiePath, requireSecure, expires));
    }

    public static void Clear(HttpResponse response, bool requireSecure)
    {
        response.Cookies.Delete(AccessTokenCookie, OptionsFor("/", requireSecure));
        response.Cookies.Delete(RefreshTokenCookie, OptionsFor(RefreshCookiePath, requireSecure));
    }

    /// <summary>Оба токена живут на одних и тех же условиях и различаются только путём и сроком.</summary>
    private static CookieOptions OptionsFor(
        string path, bool requireSecure, DateTimeOffset? expires = null) =>
        new()
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = path,
            Expires = expires,
        };
}
