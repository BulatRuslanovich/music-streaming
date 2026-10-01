// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Services;

public sealed class PlaybackSessionRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PlaybackHolder> _holders = [];

    public PlaybackHolder Claim(Guid userId, string deviceId)
    {
        var holder = new PlaybackHolder(deviceId);
        PlaybackHolder? previous;

        lock (_gate)
        {
            _holders.TryGetValue(userId, out previous);
            _holders[userId] = holder;
        }

        if (previous is not null && previous.DeviceId != deviceId)
            previous.Displace(deviceId);

        return holder;
    }

    public void Release(Guid userId, PlaybackHolder holder)
    {
        lock (_gate)
        {
            if (_holders.TryGetValue(userId, out var current) && ReferenceEquals(current, holder))
                _holders.Remove(userId);
        }
    }
}

public sealed class PlaybackHolder(string deviceId)
{
    private readonly TaskCompletionSource _displaced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string DeviceId { get; } = deviceId;
    public string? DisplacedBy { get; private set; }

    internal void Displace(string byDeviceId)
    {
        DisplacedBy = byDeviceId;
        _displaced.TrySetResult();
    }

    public async Task<bool> WasDisplacedAsync(TimeSpan within, CancellationToken ct)
    {
        try
        {
            await _displaced.Task.WaitAsync(within, ct);
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
