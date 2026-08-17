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

    /// <summary>
    /// Рутинный ли это запрос — тот, что повторяется сам по себе и одним своим существованием
    /// ничего не сообщает.
    ///
    /// <para>
    /// Compose стучится в <c>/health</c> раз в 15 секунд, Prometheus в <c>/metrics</c> — раз в
    /// тридцать: около шести строк в минуту, в которых никогда ничего не написано. За ночь они
    /// вытесняют из лога всё остальное. Сюда же перемотка: плеер режет поток на десятки диапазонных
    /// запросов, и каждый из них — тоже строка ни о чём.
    /// </para>
    ///
    /// <para>
    /// Понижены до Debug, а не выброшены: сорванная проверка живости отвечает пятисоткой и до этой
    /// ветки не доходит — её по-прежнему видно как Error. А если понадобится и остальное, хватит
    /// поднять минимальный уровень.
    /// </para>
    /// </summary>
    private static bool Routine(HttpContext context) =>
        context.Request.Path.StartsWithSegments(RequestPipelineSetup.HealthPath) ||
        context.Request.Path.StartsWithSegments(MetricsSetup.ScrapePath) ||
        (context.Request.Path.StartsWithSegments("/api/tracks") &&
         context.Request.Headers.ContainsKey("Range"));

    /// <summary>
    /// Ушёл ли клиент сам, не дослушав ответ.
    ///
    /// <para>
    /// Прерванный запрос всплывает как исключение, а по одному этому признаку он метился ошибкой —
    /// со стектрейсом на каждую перемотку. Для потока управления воспроизведением это стало
    /// невыносимо: он рвётся при каждой паузе, потому что закрытое соединение и есть «я больше не
    /// играю». Настоящая поломка отличается тем, что исключение у неё другое, и её метка Error
    /// остаётся на месте.
    /// </para>
    /// </summary>
    private static bool WentAway(HttpContext context, Exception? ex) =>
        context.RequestAborted.IsCancellationRequested && ex is null or OperationCanceledException;
}
