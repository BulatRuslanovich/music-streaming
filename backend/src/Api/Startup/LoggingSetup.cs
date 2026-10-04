// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Serilog;
using Serilog.Events;

namespace Api.Startup;

public static class LoggingSetup
{
    public const string ProblemItem = "Problem";

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
            .WriteTo.Console(
                outputTemplate:
                "[{Timestamp:HH:mm:ss}] " +
                "{Level:u3} " +
                "{Message:lj} " +
                "{NewLine}" +
                "{Exception}"));
    }

    // Одна строка на запрос. Успешные запросы — Debug: события предметной области пишут сами сервисы,
    // а поток GET-ов за обложками, сегментами HLS и лентой не должен топить их в Information.
    public static void UseApiRequestLogging(this IApplicationBuilder app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (httpContext, _, ex) =>
                httpContext.RequestAborted.IsCancellationRequested && ex is null or OperationCanceledException
                    ? LogEventLevel.Debug
                    : ex is not null || httpContext.Response.StatusCode >= 500
                        ? LogEventLevel.Error
                        // 401 — штатное истечение access-токена; отказы входа и refresh пишет AuthService.
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
