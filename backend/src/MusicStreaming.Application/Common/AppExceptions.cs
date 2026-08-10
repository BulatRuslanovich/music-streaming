namespace MusicStreaming.Application.Common;

/// <summary>Base type for failures that map to a deliberate HTTP status rather than a 500.</summary>
public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
}

public sealed class NotFoundException(string message = "The requested resource was not found.")
    : AppException(message)
{
    public override int StatusCode => 404;
}

public sealed class ValidationException(string message) : AppException(message)
{
    public override int StatusCode => 400;
}

public sealed class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => 409;
}

public sealed class AuthenticationException(string message = "Invalid credentials.")
    : AppException(message)
{
    public override int StatusCode => 401;
}

public sealed class UploadTooLargeException(long maxBytes)
    : AppException($"The file exceeds the {maxBytes / (1024 * 1024)} MB upload limit.")
{
    public override int StatusCode => 413;
}
