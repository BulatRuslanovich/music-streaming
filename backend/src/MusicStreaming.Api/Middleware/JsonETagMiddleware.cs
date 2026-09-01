// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace MusicStreaming.Api.Middleware;

/// <summary>
/// Считает ETag по телу JSON-ответа и отвечает 304, если у клиента уже есть эта версия.
/// </summary>
/// <remarks>
/// Работы серверу это не экономит — только байты, зато все. На узком канале возврат на уже
/// виденную страницу перестаёт стоить полного тела ответа, и на этом же стоит stale-while-revalidate
/// в service worker: он ревалидирует фоном, и ревалидация почти всегда упирается в 304.
/// </remarks>
public class JsonETagMiddleware(RequestDelegate next)
{
    // Ответы крупнее этого не буферизуются: они и не встречаются на путях, где 304 что-то решает.
    private const int MaxBufferedBytes = 4 * 1024 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffering = new JsonBufferingStream(context.Response, originalBody, MaxBufferedBytes);
        context.Response.Body = buffering;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        // HasStarted означает, что заголовки уже ушли — какой-то путь завершил ответ сам
        // (CompleteAsync, обрыв клиента). Трогать их нельзя, остаётся дослать накопленное.
        if (context.Response.HasStarted
            || buffering.Buffered is not { } payload
            || context.Response.StatusCode != StatusCodes.Status200OK)
        {
            await buffering.FlushToTargetAsync(context.RequestAborted);
            return;
        }

        var etag = $"W/\"{Convert.ToHexString(SHA256.HashData(payload.Span))[..32].ToLowerInvariant()}\"";
        context.Response.Headers.ETag = etag;

        if (Matches(context.Request.Headers.IfNoneMatch, etag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = null;
            context.Response.Headers.Remove(HeaderNames.ContentType);
            return;
        }

        context.Response.ContentLength = payload.Length;
        await originalBody.WriteAsync(payload, context.RequestAborted);
    }

    private static bool Matches(IEnumerable<string?> ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (string.IsNullOrEmpty(header))
                continue;

            foreach (var candidate in header.Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || string.Equals(trimmed, etag, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }
}
