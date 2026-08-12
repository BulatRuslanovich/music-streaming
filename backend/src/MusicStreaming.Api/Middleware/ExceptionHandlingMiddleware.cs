using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
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
