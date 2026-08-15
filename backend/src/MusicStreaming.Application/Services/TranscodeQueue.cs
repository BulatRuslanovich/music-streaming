using System.Collections.Concurrent;
using System.Threading.Channels;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public record TranscodeRequest(string ContentHash, string SourceRelativePath, AudioQuality Quality)
{
    /// <summary>Ключ незавершённой работы: один и тот же трек в разных ступенях — это разные задания.</summary>
    public string Key => $"{ContentHash}:{Quality}";
}

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
        if (!_pending.TryAdd(request.Key, 0))
            return false;

        if (_channel.Writer.TryWrite(request))
            return true;

        _pending.TryRemove(request.Key, out _);
        return false;
    }

    public IAsyncEnumerable<TranscodeRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkFinished(TranscodeRequest request) => _pending.TryRemove(request.Key, out _);
}
