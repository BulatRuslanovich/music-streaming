using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Api.Auth;

public static class AuthCookies
{
    public const string AccessTokenCookie = "ms_access";
    public const string RefreshTokenCookie = "ms_refresh";
    private const string RefreshCookiePath = "/api/auth";

    /// <summary>
    /// Помечать ли куки как <c>Secure</c>.
    ///
    /// <para>
    /// Вне разработки — всегда; в разработке — только когда запрос и правда пришёл по https, иначе
    /// браузер не вернёт куку на <c>http://localhost</c> и вход выглядел бы как молча не сработавший.
    /// Условие одно на все куки приложения, поэтому и живёт в одном месте.
    /// </para>
    /// </summary>
    public static bool RequireSecure(HttpRequest request, IWebHostEnvironment environment) =>
        !environment.IsDevelopment() || request.IsHttps;

    public static void Write(HttpResponse response, AuthResultDto auth, bool requireSecure)
    {
        response.Cookies.Append(AccessTokenCookie, auth.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = auth.RefreshTokenExpiresAt,
        });

        response.Cookies.Append(RefreshTokenCookie, auth.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = auth.RefreshTokenExpiresAt,
        });
    }

    public static void Clear(HttpResponse response, bool requireSecure)
    {
        response.Cookies.Delete(AccessTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

        response.Cookies.Delete(RefreshTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = requireSecure,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
        });
    }
}
