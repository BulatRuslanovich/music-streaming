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

    /**
     * Refresh-кука обязана быть видна на путях страниц, а не только на /api/auth.
     *
     * Решение о допуске принимает middleware фронтенда, и работает он ровно на навигациях —
     * его матчер исключает /api. Пока кука была заперта в /api/auth, браузер не присылал её
     * туда ни разу: `sessionGate` видел «refresh-куки нет» и разворачивал на /login сразу
     * после успешного входа. По той же причине никогда не срабатывало и обновление сессии
     * перед серверным рендером — middleware пробрасывает дальше тот же заголовок cookie.
     *
     * Узкий путь не был защитой от XSS (от него защищает HttpOnly) — он лишь ограничивал,
     * куда кука ездит. Ценой этого ограничения оказался неработающий вход.
     */
    private const string RefreshCookiePath = "/";

    /// <summary>Прежний путь. Удаление куки привязано к пути: без явного гашения браузер
    /// продолжит слать вторую копию под тем же именем, и на /api/auth они столкнутся.</summary>
    private const string LegacyRefreshCookiePath = "/api/auth";

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

        // Копия под старым путём осталась бы жить своей жизнью и приезжала бы на /api/auth
        // вместе с новой, под тем же именем. Гасим её здесь же, при каждой выдаче.
        response.Cookies.Delete(
            RefreshTokenCookie, OptionsFor(LegacyRefreshCookiePath, requireSecure));
    }

    public static void Clear(HttpResponse response, bool requireSecure)
    {
        response.Cookies.Delete(AccessTokenCookie, OptionsFor("/", requireSecure));
        response.Cookies.Delete(RefreshTokenCookie, OptionsFor(RefreshCookiePath, requireSecure));
        response.Cookies.Delete(
            RefreshTokenCookie, OptionsFor(LegacyRefreshCookiePath, requireSecure));
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
