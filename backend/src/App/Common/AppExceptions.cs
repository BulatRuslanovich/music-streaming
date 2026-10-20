// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Common;

// Сообщение — английский шаблон с {0}, {1} и аргументы. Message (для логов) всегда по-английски,
// LocalizedMessage — на языке запроса (ErrorText).
public abstract class AppException(string template, params object?[] args)
    : Exception(ErrorText.Format(template, args))
{
    public string Template { get; } = template;

    public IReadOnlyList<object?> Arguments { get; } = args;

    public string LocalizedMessage => ErrorText.Localize(Template, Arguments);

    public abstract int StatusCode { get; }
}

public class NotFoundException(string message = "The requested resource was not found.", params object?[] args)
    : AppException(message, args)
{
    public override int StatusCode => 404;
}

public class ValidationException(string message, params object?[] args) : AppException(message, args)
{
    public override int StatusCode => 400;
}

public class ConflictException(string message, params object?[] args) : AppException(message, args)
{
    public override int StatusCode => 409;
}

public class AuthenticationException(string message = "Invalid credentials.", params object?[] args)
    : AppException(message, args)
{
    public override int StatusCode => 401;
}

public class ForbiddenException(string message, params object?[] args) : AppException(message, args)
{
    public override int StatusCode => 403;
}

public class UploadTooLargeException(long maxBytes)
    : AppException("The file exceeds the {0} MB upload limit.", maxBytes / (1024 * 1024))
{
    public override int StatusCode => 413;
}
