namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Приведение изображений к тому виду, в котором их хранит и отдаёт сервис: квадрат, webp,
/// фиксированный набор сторон.
///
/// <para>
/// Обложки приходят из тегов аудиофайлов и от пользователей, то есть в любом формате и любого
/// размера. Приведение к одному виду при записи, а не при отдаче, означает, что путь чтения — это
/// просто отдача файла с диска.
/// </para>
/// </summary>
public interface IImageProcessor
{
    Task<byte[]> ToSquareWebpAsync(Stream source, int edge, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
        Stream source, IReadOnlyList<int> edges, CancellationToken cancellationToken = default);
}

public record ResizedImage(int Edge, byte[] Content);
