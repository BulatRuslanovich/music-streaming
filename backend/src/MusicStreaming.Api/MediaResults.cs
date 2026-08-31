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
        // Обложка живёт ровно столько, сколько её файл: механизм сброса у клиента свой — при
        // редактировании к URL дописывается ?v= (см. media.ts). Сутки здесь означали лишнюю
        // ревалидацию каждой картинки в сетке на следующий день.
        controller.Response.Headers.CacheControl = "private, max-age=2592000, stale-while-revalidate=31536000";

        return new FileStreamResult(image.Content, image.ContentType)
        {
            EntityTag = EntityTagHeaderValue.Parse(image.ETag),
        };
    }
}
