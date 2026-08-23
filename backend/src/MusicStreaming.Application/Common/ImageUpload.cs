// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Application.Common;

public static class ImageUpload
{
    public const int Edge = 640;

    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// Принимает загруженное изображение: проверяет тип и размер, а затем приводит его к тому
    /// квадратному webp, в котором хранятся все обложки и фотографии.
    /// </summary>
    public static async Task<byte[]> AcceptSquareWebpAsync(
        IImageProcessor imageProcessor,
        Stream content,
        string? contentType,
        string fileName,
        long length,
        long maxBytes,
        CancellationToken ct)
    {
        Validate(contentType, fileName, length, maxBytes);

        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        return await imageProcessor.ToSquareWebpAsync(buffered, Edge, ct);
    }

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
