// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Serilog;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace Api.Startup;

public static class LoggingSetup
{
    public const string ProblemItem = "Problem";

    // Палитра Code, но уровни различимы с первого взгляда: INF спокойный, WRN и ERR бросаются в глаза.
    private static readonly TemplateTheme Theme = new(TemplateTheme.Code, new Dictionary<TemplateThemeStyle, string>
    {
        [TemplateThemeStyle.LevelVerbose] = "\x1b[38;5;0240m",
        [TemplateThemeStyle.LevelDebug] = "\x1b[38;5;0244m",
        [TemplateThemeStyle.LevelInformation] = "\x1b[38;5;0075m",
        [TemplateThemeStyle.LevelWarning] = "\x1b[38;5;0220;1m",
        [TemplateThemeStyle.LevelError] = "\x1b[38;5;0231;48;5;0160;1m",
        [TemplateThemeStyle.LevelFatal] = "\x1b[38;5;0231;48;5;0127;1m",
    });

    public static void UseApiSerilog(this IHostBuilder host)
    {
        host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            // Сбои команд и SaveChanges всегда долетают до вызывающего кода исключением, а конфликты
            // уникальности здесь — штатная ветка (гонка тегов при загрузке, переименование в занятое имя).
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", LogEventLevel.Fatal)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .WriteTo.Console(new ExpressionTemplate(
                "{@t:HH:mm:ss} {@l:u3} " +
                "{if SourceContext = 'Serilog.AspNetCore.RequestLoggingMiddleware' then 'http' " +
                "else if StartsWith(SourceContext, 'Microsoft.Hosting') then 'host' " +
                "else Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),-24} " +
                "{@m}\n{@x}",
                theme: Theme,
                // NO_COLOR (no-color.org) отключает цвета; иначе они остаются и в `docker compose logs`.
                applyThemeWhenOutputIsRedirected: Environment.GetEnvironmentVariable("NO_COLOR") is null)));
    }

    public static void UseApiRequestLogging(this IApplicationBuilder app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, ex) =>
                httpContext.RequestAborted.IsCancellationRequested && ex is null or OperationCanceledException
                    ? LogEventLevel.Debug
                    : ex is not null || httpContext.Response.StatusCode >= 500
                        ? LogEventLevel.Error
                        : httpContext.Response.StatusCode is >= 400 and not 401
                            ? LogEventLevel.Information
                            : LogEventLevel.Debug;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                diagnosticContext.Set(
                    ProblemItem,
                    httpContext.Items[ProblemItem] is string problem ? $": {problem}" : string.Empty);

            options.MessageTemplate = "{RequestMethod} {RequestPath} → {StatusCode} ({Elapsed:0.0} ms){Problem}";
        });
    }
}
