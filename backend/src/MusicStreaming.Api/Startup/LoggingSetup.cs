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
            options.GetLevel = (httpContext, elapsed, ex) =>
                ex is not null
                    ? LogEventLevel.Error
                    : httpContext.Response.StatusCode >= 500
                        ? LogEventLevel.Error
                        : httpContext.Request.Path.StartsWithSegments("/api/tracks") &&
                          httpContext.Request.Headers.ContainsKey("Range")
                            ? LogEventLevel.Debug
                            : LogEventLevel.Information;

            options.MessageTemplate = "{RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0} ms)";
        });
}
