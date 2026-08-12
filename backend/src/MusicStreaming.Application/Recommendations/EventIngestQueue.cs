using System.Threading.Channels;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Передаёт принятые события воркеру записи, не заставляя запрос ждать обращения к базе.
/// Телеметрия ни при каких обстоятельствах не должна замедлять воспроизведение.
///
/// <para>
/// Канал ограничен и при переполнении отбрасывает записи, а не блокирует. Потерянный при всплеске
/// скип стоит доли одного профиля; остановленный конвейер запросов стоит сессии.
/// </para>
/// </summary>
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

    /// <summary>События, отброшенные из-за переполнения очереди, — публикуются как метрика.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public bool TryEnqueue(PlaybackEvent playbackEvent)
    {
        if (_channel.Writer.TryWrite(playbackEvent))
            return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public IAsyncEnumerable<PlaybackEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Ждёт хотя бы одно событие, затем забирает до <paramref name="maxBatchSize"/> из того, что
    /// уже стоит в очереди. Батчинг превращает всплеск скипов в одну вставку.
    /// </summary>
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

/// <summary>
/// Отслеживает, у каких пользователей есть необработанная активность.
///
/// <para>
/// Множество, а не очередь, потому что единица работы — «этот пользователь устарел», а не «это
/// событие нужно обработать»: слушатель, пролистывающий альбом, помечает себя сорок раз и всё
/// равно должен быть пересчитан один раз. Хранится момент <em>первой</em> пометки с прошлой
/// перестройки, из-за чего дебаунс работает как ограничение частоты, а не как ожидание тишины:
/// тот, кто слушает непрерывно, всё равно пересчитывается с фиксированной периодичностью, а не
/// никогда.
/// </para>
/// </summary>
public class RecommendationRefreshQueue
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> _dirty = new();

    /// <summary>Отмечает, что профиль пользователя больше не отражает его активность.</summary>
    public void MarkDirty(Guid userId, DateTimeOffset at) =>
        _dirty.TryAdd(userId, at);

    /// <summary>Пользователи, ожидающие пересчёта.</summary>
    public int PendingCount => _dirty.Count;

    /// <summary>
    /// Забирает всех, чья активность закончилась не менее <paramref name="debounce"/> назад.
    /// Забранные удаляются, так что следующее событие снова пометит их и назначит новый проход.
    /// </summary>
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
