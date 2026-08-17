using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MusicStreaming.Api.Auth;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Security;

namespace MusicStreaming.Api.Startup;

/// <summary>
/// Кто выполняет запрос и что ему можно.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>
    /// Настраивает проверку токена и правила доступа.
    ///
    /// <para>
    /// Главное решение здесь — <c>SetFallbackPolicy</c>: ручка закрыта, пока с неё явно не сняли
    /// защиту через <c>[AllowAnonymous]</c>. Обратный порядок («открыто, пока не закрыли») ошибается
    /// молча — забытый атрибут оставляет данные наружу, и никто этого не заметит. При закрытом по
    /// умолчанию забытый <c>[AllowAnonymous]</c> даёт 401 на первом же обращении.
    /// </para>
    /// </summary>
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Иначе библиотека переименовывает claims в длинные имена вида
                // http://schemas.xmlsoap.org/..., и константы из AppClaims перестают совпадать с
                // тем, что лежит в токене.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.BuildSigningKey(jwt),
                    ValidateLifetime = true,

                    // Пять минут по умолчанию — слишком щедро для токена, живущего минуты: он
                    // продолжал бы действовать заметную долю собственного срока после истечения.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = AppClaims.Username,
                    RoleClaimType = AppClaims.Role,
                };

                // Токен принимается и из куки, а не только из заголовка. Без этого браузер не смог
                // бы получить ни обложку, ни аудиопоток: <img> и <audio> заголовок задать не
                // позволяют, а обходные пути — токен в адресе или загрузка трека целиком в память —
                // означают либо утечку токена в логи прокси, либо потерю перемотки.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token) &&
                            context.Request.Cookies.TryGetValue(AuthCookies.AccessTokenCookie, out var cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AppPolicies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.Admin));

        return services;
    }
}
