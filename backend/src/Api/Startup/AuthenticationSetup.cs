// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using App.Options;
using Infrastructure.Persistence;
using Infrastructure.Security;

namespace Api.Startup;


public static class AuthenticationSetup
{
    public static void AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
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
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = "username",
                    RoleClaimType = "role",
                };

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
                    // Access tokens are stateless, so revoking sessions or deactivating a user
                    // would otherwise only take effect once the token expires. "sid" is the
                    // refresh token issued alongside; revoking a session deletes that row.
                    OnTokenValidated = async context =>
                    {
                        if (!Guid.TryParse(context.Principal!.FindFirst("sid")?.Value, out var sessionId))
                        {
                            context.Fail("Malformed access token.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                        var alive = await db.RefreshTokens.AnyAsync(
                            t => t.Id == sessionId && t.User!.IsActive,
                            context.HttpContext.RequestAborted);

                        if (!alive)
                            context.Fail("Session was revoked.");
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy("Admin", policy => policy
                .RequireAuthenticatedUser()
                .RequireRole("Admin"));
    }
}
