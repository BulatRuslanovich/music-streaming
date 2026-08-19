// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace MusicStreaming.Api.Startup;

public static class RequestPipelineSetup
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var loginAttempts = configuration.GetValue("Security:LoginAttemptsPerMinute", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginAttempts,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy("events", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.Identity?.Name
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    public static IServiceCollection AddApiForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
                           ?? ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

            foreach (var network in networks)
            {
                var parts = network.Split('/', 2);
                if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix)
                                      && int.TryParse(parts[1], out var length))
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, length));
                }
            }
        });

        return services;
    }
}
