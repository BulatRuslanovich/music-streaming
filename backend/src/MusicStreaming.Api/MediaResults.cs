using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api;

public static class MediaResults
{
    private const string ImageCacheControl = "private, max-age=86400, stale-while-revalidate=604800";

    /// <summary>
    /// Загруженная картинка, про которую уже известно, что она есть. Пустое поле формы и вовсе
    /// пропущенный файл приходят одинаково часто — оба означают, что отправлять было нечего, — и
    /// отвечать на них стоит одинаково, а не падать позже на чтении пустого потока.
    /// </summary>
    public static IFormFile RequireImage(this IFormFile? file) =>
        file is { Length: > 0 } present
            ? present
            : throw new ValidationException("No image was provided.");

    public static IActionResult ImageFile(this ControllerBase controller, CoverResult image)
    {
        controller.Response.Headers.CacheControl = ImageCacheControl;

        return new FileStreamResult(image.Content, image.ContentType)
        {
            EntityTag = EntityTagHeaderValue.Parse(image.ETag),
        };
    }
}
