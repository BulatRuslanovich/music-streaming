// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace Api.Middleware;

public class JsonETagMiddleware(RequestDelegate next)
{
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

        if (context.Response.HasStarted
            || buffering.Buffered is not { } payload
            || context.Response.StatusCode != StatusCodes.Status200OK)
        {
            await buffering.FlushToTargetAsync(context.RequestAborted);
            return;
        }

        var etag = $"W/\"{Convert.ToHexString(SHA256.HashData(payload.Span))[..32].ToLowerInvariant()}\"";
        context.Response.Headers.ETag = etag;

        var matches = context.Request.Headers.IfNoneMatch
            .SelectMany(header => (header ?? string.Empty).Split(','))
            .Select(candidate => candidate.Trim())
            .Any(candidate => candidate == "*" || candidate == etag);

        if (matches)
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            context.Response.ContentLength = null;
            context.Response.Headers.Remove(HeaderNames.ContentType);
            return;
        }

        context.Response.ContentLength = payload.Length;
        await originalBody.WriteAsync(payload, context.RequestAborted);
    }
}
