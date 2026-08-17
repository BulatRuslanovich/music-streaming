using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Api.Middleware;

/// <summary>
/// Превращает исключения в ответы. Единственное место, где это происходит.
///
/// <para>
/// Сервисы не знают про HTTP и сообщают об отказе исключением из иерархии <see cref="AppException"/>,
/// которое несёт в себе код ответа. Благодаря этому в контроллерах нет ни одной проверки вида
/// «не нашли — вернуть 404»: решение принято там, где обнаружена причина, а сюда доходит уже
/// готовый код. Формат тела — RFC 7807 (<c>application/problem+json</c>), один на все ошибки.
/// </para>
///
/// <para>
/// Стоит в конвейере раньше логирования запросов, то есть оборачивает его: логгер должен увидеть
/// итоговый статус (404), а не исключение. Иначе каждый обычный «не найдено» попадал бы в лог
/// стектрейсом.
/// </para>
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    /// <summary>Пропускает запрос дальше и переводит всё, чем он мог закончиться, в ответ.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            logger.LogInformation(
                "Request {Method} {Path} failed with {Status}: {Message}",
                context.Request.Method, context.Request.Path, ex.StatusCode, ex.Message);

            await WriteProblemAsync(context, ex.StatusCode, TitleFor(ex.StatusCode), ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Бросается слоем хранилища, когда путь вышел бы за пределы его корня.
            logger.LogError(ex, "Blocked storage access for {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden", "Access denied.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Слушатель перемотал или ушёл со страницы посреди потока; не та ошибка, о которой
            // стоит громко писать в лог.
            logger.LogDebug("Request {Path} aborted by the client", context.Request.Path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Пишет тело ошибки в формате RFC 7807.
    ///
    /// <para>
    /// Уже начатый ответ не трогается вовсе: аудиопоток отдаётся кусками, и приписать к нему JSON
    /// посреди передачи значило бы отдать слушателю испорченный файл вместо честного обрыва.
    /// </para>
    /// </summary>
    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };

        await context.Response.WriteAsJsonAsync(problem);
    }

    /// <summary>Заголовок ошибки по её коду — стандартные названия статусов HTTP.</summary>
    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        _ => "Error",
    };
}
