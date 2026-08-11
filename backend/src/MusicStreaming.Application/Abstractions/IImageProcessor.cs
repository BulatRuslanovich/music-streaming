namespace MusicStreaming.Application.Abstractions;

public interface IImageProcessor
{
    Task<byte[]> ToSquareWebpAsync(Stream source, int edge, CancellationToken cancellationToken = default);
}
