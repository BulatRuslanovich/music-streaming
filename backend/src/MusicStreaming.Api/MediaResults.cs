using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api;

public static class MediaResults
{
    private const string ImageCacheControl = "private, max-age=86400, stale-while-revalidate=604800";

    public static IActionResult ImageFile(this ControllerBase controller, CoverResult image)
    {
        controller.Response.Headers.CacheControl = ImageCacheControl;

        return new FileStreamResult(image.Content, image.ContentType)
        {
            EntityTag = EntityTagHeaderValue.Parse(image.ETag),
        };
    }
}
