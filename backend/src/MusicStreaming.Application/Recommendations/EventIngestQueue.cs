using System.Threading.Channels;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

public class EventIngestQueue
{
    private const int Capacity = 8192;

    private readonly Channel<PlaybackEvent> _channel =
        Channel.CreateBounded<PlaybackEvent>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private long _dropped;

    public bool TryEnqueue(PlaybackEvent playbackEvent)
    {
        if (_channel.Writer.TryWrite(playbackEvent))
            return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public async Task<List<PlaybackEvent>> ReadBatchAsync(int maxBatchSize, CancellationToken cancellationToken)
    {
        var batch = new List<PlaybackEvent>(Math.Min(maxBatchSize, 64));

        if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            return batch;

        while (batch.Count < maxBatchSize && _channel.Reader.TryRead(out var next))
            batch.Add(next);

        return batch;
    }
}

public class RecommendationRefreshQueue
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _dirty = new();

    public void MarkDirty(Guid userId, DateTimeOffset at) =>
        _dirty.TryAdd(userId, at);

    public IReadOnlyList<Guid> ClaimSettled(DateTimeOffset now, TimeSpan debounce)
    {
        var settled = new List<Guid>();

        foreach (var (userId, markedAt) in _dirty)
        {
            if (now - markedAt < debounce)
                continue;

            if (_dirty.TryRemove(userId, out _))
                settled.Add(userId);
        }

        return settled;
    }
}
