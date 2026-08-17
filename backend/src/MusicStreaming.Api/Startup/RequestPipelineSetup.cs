using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace MusicStreaming.Api.Startup;

public static class RequestPipelineSetup
{
    public const string DevCorsPolicy = "dev-frontend";

    /// <summary>
    /// Где живёт проверка живости. Вынесена в константу, потому что кроме маршрута её знает ещё и
    /// логирование запросов — чтобы опрос по ней не попадал в лог (см. <c>LoggingSetup</c>).
    /// </summary>
    public const string HealthPath = "/health";

    private static readonly string[] DefaultTrustedNetworks =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    /// <summary>
    /// Сколько попыток входа в минуту с одного адреса. Настраивается, потому что за общим выходом
    /// в интернет — из-под NAT или VPN — под одним адресом живёт вся семья, и десяти попыток на
    /// всех может не хватить.
    /// </summary>
    private const int DefaultLoginAttemptsPerMinute = 10;

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        var loginAttempts = configuration.GetValue(
            "Security:LoginAttemptsPerMinute", DefaultLoginAttemptsPerMinute);

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

            // Телеметрию клиент шлёт пачками, поэтому исправному плееру хватает нескольких запросов
            // в минуту. Лимит достаточно щедрый для всплеска пропусков и достаточно низкий, чтобы
            // сорвавшаяся вкладка не залила очередь приёма. Разделён по пользователям, а не по
            // адресам, чтобы жильцы одной квартиры не делили общий бюджет.
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
                           ?? DefaultTrustedNetworks;

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

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration) =>
        services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                         ?? ["http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
}
