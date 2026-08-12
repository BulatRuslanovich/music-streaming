using MusicStreaming.Application.Recommendations;
using OpenTelemetry.Metrics;

namespace MusicStreaming.Api.Startup;

/// <summary>
/// Публикует метрики среды выполнения и рекомендаций, чтобы их забирал Prometheus.
/// </summary>
public static class MetricsSetup
{
    /// <summary>
    /// Где живёт эндпойнт для сбора метрик.
    ///
    /// <para>
    /// Намеренно не под <c>/api</c>: обратный прокси пробрасывает наружу только <c>/api/*</c> и
    /// <c>/health</c>, поэтому именно вынос пути за этот префикс делает эндпойнт доступным изнутри
    /// сети compose и больше ниоткуда.
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
        // Анонимный, потому что иначе его закрыла бы политика авторизации по умолчанию, а учётной
        // записи у Prometheus нет. Наружу он не выставлен — см. ScrapePath.
        app.MapPrometheusScrapingEndpoint(ScrapePath).AllowAnonymous();

        return app;
    }
}
