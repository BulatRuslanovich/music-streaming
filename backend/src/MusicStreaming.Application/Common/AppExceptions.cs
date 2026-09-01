// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;


public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
}

public class NotFoundException(string message = "The requested resource was not found.")
    : AppException(message)
{
    public override int StatusCode => 404;
}

public class ValidationException(string message) : AppException(message)
{
    public override int StatusCode => 400;
}

public class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => 409;
}

public class AuthenticationException(string message = "Invalid credentials.")
    : AppException(message)
{
    public override int StatusCode => 401;
}

public class ForbiddenException(string message) : AppException(message)
{
    public override int StatusCode => 403;
}

public class UploadTooLargeException(long maxBytes)
    : AppException($"The file exceeds the {maxBytes / (1024 * 1024)} MB upload limit.")
{
    public override int StatusCode => 413;
}

/// <summary>
/// Возможность выключена или её внешняя зависимость недоступна — например, нет ffmpeg, и адаптивный
/// поток отдать нечем. Не ошибка запроса: тот же запрос заработает, как только зависимость появится.
/// </summary>
public class ServiceUnavailableException(string message) : AppException(message)
{
    public override int StatusCode => 503;
}
