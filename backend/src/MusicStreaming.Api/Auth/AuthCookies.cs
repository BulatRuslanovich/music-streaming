using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Api.Auth;

/// <summary>
/// Carries the JWT in an HttpOnly cookie in addition to returning it in the login response.
///
/// This is what makes authenticated streaming possible: an HTML5 <c>&lt;audio&gt;</c> element
/// cannot attach an <c>Authorization</c> header to its own request, so a token kept only in
/// JavaScript could never protect <c>/api/tracks/{id}/stream</c>. A cookie is sent automatically
/// by the media request, and being HttpOnly it is also out of reach of injected script — unlike
/// a token in <c>localStorage</c>.
///
/// <c>SameSite=Lax</c> is what stands in for CSRF tokens here: it keeps the cookie off
/// cross-site POST/PUT/DELETE requests, while still allowing normal top-level navigation to the
/// app. Same-origin is a hard requirement for that to hold, which is why the reverse proxy serves
/// the API under <c>/api</c> on the very same host as the frontend.
/// </summary>
public static class AuthCookies
{
    public const string AccessTokenCookie = "ms_access";
    public const string RefreshTokenCookie = "ms_refresh";

    /// <summary>Refresh is only ever sent to the endpoints that need it.</summary>
    private const string RefreshCookiePath = "/api/auth";

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
