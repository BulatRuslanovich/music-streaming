using MusicStreaming.Application.Recommendations;
using OpenTelemetry.Metrics;

namespace MusicStreaming.Api.Startup;

/// <summary>
/// Publishes runtime and recommendation metrics for Prometheus to scrape.
/// </summary>
public static class MetricsSetup
{
    /// <summary>
    /// Where the scrape endpoint lives.
    ///
    /// <para>
    /// Deliberately not under <c>/api</c>: the reverse proxy only forwards <c>/api/*</c> and
    /// <c>/health</c> from the public interface, so keeping the path outside that prefix is what
    /// makes the endpoint reachable from inside the compose network and from nowhere else.
    /// </para>
    /// </summary>
    public const string ScrapePath = "/metrics";

    public static IServiceCollection AddApiMetrics(this IServiceCollection services)
    {
        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(RecommendationMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter());

        return services;
    }

    public static WebApplication MapApiMetrics(this WebApplication app)
    {
        // Anonymous because the fallback authorisation policy would otherwise close it, and
        // Prometheus has no account. It is not exposed publicly — see ScrapePath.
        app.MapPrometheusScrapingEndpoint(ScrapePath).AllowAnonymous();

        return app;
    }
}
