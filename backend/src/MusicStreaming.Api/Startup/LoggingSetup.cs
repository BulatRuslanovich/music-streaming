using Serilog;
using Serilog.Events;

namespace MusicStreaming.Api.Startup;

public static class LoggingSetup
{
    public static IHostBuilder UseApiSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .WriteTo.Console(
                outputTemplate:
                    "[{Timestamp:HH:mm:ss}] " +
                    "{Level:u3} " +
                    "{Message:lj} " +
                    "{NewLine}" +
                    "{Exception}"));

    public static IApplicationBuilder UseApiRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, ex) =>
                WentAway(httpContext, ex)
                    ? LogEventLevel.Debug
                    : ex is not null
                        ? LogEventLevel.Error
                        : httpContext.Response.StatusCode >= 500
                            ? LogEventLevel.Error
                            : Routine(httpContext)
                                ? LogEventLevel.Debug
                                : LogEventLevel.Information;

            options.MessageTemplate = "{RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0} ms)";
        });

    private static bool Routine(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/health") ||
        context.Request.Path.StartsWithSegments("/metrics") ||
        (context.Request.Path.StartsWithSegments("/api/tracks") &&
         context.Request.Headers.ContainsKey("Range"));

    private static bool WentAway(HttpContext context, Exception? ex) =>
        context.RequestAborted.IsCancellationRequested && ex is null or OperationCanceledException;
}
