using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MusicStreaming.Application.Services;

public record TranscodeRequest(string ContentHash, string SourceRelativePath);

public class TranscodeQueue
{
    private const int Capacity = 128;

    private readonly Channel<TranscodeRequest> _channel =
        Channel.CreateBounded<TranscodeRequest>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    public bool TryEnqueue(TranscodeRequest request)
    {
        if (!_pending.TryAdd(request.ContentHash, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _pending.TryRemove(request.ContentHash, out _);
        return false;
    }

    public IAsyncEnumerable<TranscodeRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkFinished(string contentHash) => _pending.TryRemove(contentHash, out _);
}
