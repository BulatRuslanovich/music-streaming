// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Api.Startup;

/// <summary>Per-minute permit counts for the rate-limited endpoints.</summary>
/// <remarks>
/// Отдельный объект в DI только ради тестов: набор логинится сотни раз в минуту с одного адреса и
/// подменяет этот экземпляр целиком. В приложении он всегда <see cref="Default"/>.
/// </remarks>
public sealed record RateLimits(int Login, int Events, int Uploads, int Searches)
{
    public static RateLimits Default { get; } = new(
        SecurityLimits.LoginAttemptsPerMinute,
        SecurityLimits.EventsPerMinute,
        SecurityLimits.UploadsPerMinute,
        SecurityLimits.SearchesPerMinute);
}

public static class RequestPipelineSetup
{
    public const string LoginPolicy = "login";
    public const string EventsPolicy = "events";
    public const string UploadPolicy = "upload";
    public const string SearchPolicy = "search";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.TryAddSingleton(RateLimits.Default);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Логин ещё анонимен, поэтому единственный ключ — адрес. Перебор одной учётки
            // с пула адресов ловит LoginAttemptTracker, а не этот лимитер.
            options.AddPolicy(LoginPolicy, context => PerMinute(
                ByAddress(context), Limits(context).Login));

            options.AddPolicy(EventsPolicy, context => PerMinute(
                ByUser(context), Limits(context).Events));

            options.AddPolicy(UploadPolicy, context => PerMinute(
                ByUser(context), Limits(context).Uploads));

            options.AddPolicy(SearchPolicy, context => PerMinute(
                ByUser(context), Limits(context).Searches));
        });

        return services;
    }

    private static RateLimitPartition<string> PerMinute(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    private static RateLimits Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<RateLimits>();

    private static string ByAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string ByUser(HttpContext context) =>
        context.User.Identity?.Name ?? ByAddress(context);

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
