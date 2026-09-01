// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;

namespace MusicStreaming.Api.Startup;

/// <summary>
/// Имя файла приходит заголовком, а не формой: тело запроса — это сами байты, и читать его как
/// multipart значило бы буферизовать весь файл ради одной строки.
/// </summary>
public static class UploadHeaders
{
    public static string FileName(HttpRequest request)
    {
        if (request.Headers["X-File-Name"].FirstOrDefault() is not { Length: > 0 } encoded)
            throw new ValidationException("The X-File-Name header is required.");

        try
        {
            return Uri.UnescapeDataString(encoded);
        }
        catch (UriFormatException)
        {
            throw new ValidationException("The X-File-Name header is not valid.");
        }
    }
}
