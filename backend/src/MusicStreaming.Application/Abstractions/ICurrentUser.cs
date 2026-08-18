namespace MusicStreaming.Application.Abstractions;

public interface ICurrentUser
{
    Guid Id { get; }

    bool IsAuthenticated { get; }
}
