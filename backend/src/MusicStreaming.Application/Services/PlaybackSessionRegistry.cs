namespace MusicStreaming.Application.Services;

public sealed class PlaybackSessionRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, PlaybackHolder> holders = [];

    public PlaybackHolder Claim(Guid userId, string deviceId)
    {
        var holder = new PlaybackHolder(deviceId);
        PlaybackHolder? previous;

        lock (gate)
        {
            holders.TryGetValue(userId, out previous);
            holders[userId] = holder;
        }

        if (previous is not null && previous.DeviceId != deviceId)
            previous.Displace(deviceId);

        return holder;
    }

    public void Release(Guid userId, PlaybackHolder holder)
    {
        lock (gate)
        {
            if (holders.TryGetValue(userId, out var current) && ReferenceEquals(current, holder))
                holders.Remove(userId);
        }
    }
}

public sealed class PlaybackHolder(string deviceId)
{
    private readonly TaskCompletionSource displaced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string DeviceId { get; } = deviceId;
    public string? DisplacedBy { get; private set; }

    internal void Displace(string byDeviceId)
    {
        DisplacedBy = byDeviceId;
        displaced.TrySetResult();
    }

    public async Task<bool> WasDisplacedAsync(TimeSpan within, CancellationToken ct)
    {
        try
        {
            await displaced.Task.WaitAsync(within, ct);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
