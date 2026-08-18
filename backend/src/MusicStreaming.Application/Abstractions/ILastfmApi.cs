namespace MusicStreaming.Application.Abstractions;

public record LastfmSession(string Username, string SessionKey);

public record LastfmTrack(
    string Artist,
    string Title,
    string? Album,
    int DurationSeconds,
    DateTimeOffset? PlayedAt);

public class LastfmException(string message, bool Transient = false, bool AuthFailure = false)
    : Exception(message)
{
    public bool IsTransient { get; } = Transient;
    public bool IsAuthFailure { get; } = AuthFailure;
}

public interface ILastfmApi
{
    bool IsConfigured { get; }
    string AuthorizeUrl(string callbackUrl);

    Task<LastfmSession> CompleteAsync(string token, CancellationToken ct = default);
    Task SendAsync(LastfmTrack track, string sessionKey, CancellationToken ct = default);
}

public interface ISecretProtector
{
    string Protect(string value);
    string? Unprotect(string protectedValue);
}
