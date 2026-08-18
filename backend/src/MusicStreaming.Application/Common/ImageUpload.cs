namespace MusicStreaming.Application.Common;

public static class ImageUpload
{
    public const int Edge = 640;

    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public static void Validate(string? contentType, string fileName, long length, long maxBytes)
    {
        if (length > maxBytes)
            throw new UploadTooLargeException(maxBytes);

        if (contentType is null || !AllowedContentTypes.Contains(contentType.ToLowerInvariant()))
            throw new ValidationException("Only JPEG, PNG and WebP images are accepted.");

        if (!AllowedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant()))
            throw new ValidationException("Only .jpg, .png and .webp files are accepted.");
    }
}
