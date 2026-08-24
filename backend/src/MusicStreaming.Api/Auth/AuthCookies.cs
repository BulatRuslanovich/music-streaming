// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Api.Auth;

public static class AuthCookies
{
    public const string AccessTokenCookie = "ms_access";
    public const string RefreshTokenCookie = "ms_refresh";
    public const string SessionHintCookie = "ms_session";
    private const string RefreshCookiePath = "/api/auth";

    private static readonly JsonSerializerOptions HintJson =
        new(JsonSerializerDefaults.Web);

    public static bool RequireSecure(HttpRequest request, IWebHostEnvironment environment) =>
        !environment.IsDevelopment() || request.IsHttps;

    public static void Write(HttpResponse response, AuthResultDto auth, bool requireSecure)
    {
        var expires = auth.RefreshTokenExpiresAt;

        response.Cookies.Append(
            AccessTokenCookie, auth.AccessToken, OptionsFor("/", requireSecure, expires));

        response.Cookies.Append(
            RefreshTokenCookie, auth.RefreshToken, OptionsFor(RefreshCookiePath, requireSecure, expires));

        response.Cookies.Append(
            SessionHintCookie, EncodeHint(auth.User), HintOptionsFor(requireSecure, expires));
    }

    public static void Clear(HttpResponse response, bool requireSecure)
    {
        response.Cookies.Delete(AccessTokenCookie, OptionsFor("/", requireSecure));
        response.Cookies.Delete(RefreshTokenCookie, OptionsFor(RefreshCookiePath, requireSecure));
        response.Cookies.Delete(SessionHintCookie, HintOptionsFor(requireSecure));
    }

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

    private static CookieOptions HintOptionsFor(bool requireSecure, DateTimeOffset? expires = null) =>
        new()
        {
            HttpOnly = false,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires,
        };

    private static string EncodeHint(UserDto user) =>
        WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(user, HintJson));
}
