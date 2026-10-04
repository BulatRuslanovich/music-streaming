// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Api.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using App.Common;
using Serilog;

namespace Api.Middleware;

// Сам ничего не пишет: причина отказа и исключение уходят в строку request-лога этого запроса.
public class ExceptionHandlingMiddleware(RequestDelegate next, IDiagnosticContext diagnosticContext)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            context.Items[LoggingSetup.ProblemItem] = ex.Message;
            await WriteProblemAsync(context, ex.StatusCode, ReasonPhrases.GetReasonPhrase(ex.StatusCode), ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            diagnosticContext.SetException(ex);
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden", "Access denied.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            diagnosticContext.SetException(ex);
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
}
