// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api;

public static class MediaResults
{
    public static IFormFile RequireImage(this IFormFile? file)
    {
        if (file is not null && file.Length > 0)
        {
            return file;
        }
        else
        {
            throw new ValidationException("No image was provided.");
        }
    }


    public static IActionResult ImageFile(this ControllerBase controller, CoverResult image)
    {
        controller.Response.Headers.CacheControl = "private, max-age=86400, stale-while-revalidate=604800";

        return new FileStreamResult(image.Content, image.ContentType)
        {
            EntityTag = EntityTagHeaderValue.Parse(image.ETag),
        };
    }
}
