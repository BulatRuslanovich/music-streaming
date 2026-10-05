// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Services;

public sealed record PlaybackState(
    string DeviceId,
    string DeviceName,
    IReadOnlyList<Guid> TrackIds,
    int Index,
    double PositionSeconds,
    bool IsPlaying,
    bool Shuffle,
    string Repeat,
    DateTimeOffset ReportedAt);

public sealed class PlaybackSessionRegistry(TimeProvider clock)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromHours(12);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PlaybackHolder> _holders = [];
    private readonly Dictionary<Guid, PlaybackState> _states = [];

    public PlaybackHolder Claim(Guid userId, string deviceId, string deviceName)
    {
        var holder = new PlaybackHolder(deviceId, deviceName);
        PlaybackHolder? previous;

        lock (_gate)
        {
            _holders.TryGetValue(userId, out previous);
            _holders[userId] = holder;
        }

        if (previous is not null && previous.DeviceId != deviceId)
            previous.Displace(holder);

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

    public bool Report(Guid userId, PlaybackState state)
    {
        lock (_gate)
        {
            var accepted = _holders.TryGetValue(userId, out var holder)
                ? holder.DeviceId == state.DeviceId
                : state.IsPlaying || !_states.TryGetValue(userId, out var last) || last.DeviceId == state.DeviceId
                  || IsAbandoned(last);

            if (accepted)
                _states[userId] = state;

            return accepted;
        }
    }

    public PlaybackState? Elsewhere(Guid userId, string deviceId)
    {
        PlaybackState? state;
        bool live;

        lock (_gate)
        {
            if (!_states.TryGetValue(userId, out state) || state.DeviceId == deviceId)
                return null;

            live = _holders.TryGetValue(userId, out var holder) && holder.DeviceId == state.DeviceId;
        }

        if (!live && IsAbandoned(state))
            return null;

        if (!state.IsPlaying)
            return state;

        if (!live)
            return state with { IsPlaying = false };

        var elapsed = clock.GetUtcNow() - state.ReportedAt;
        return state with { PositionSeconds = state.PositionSeconds + Math.Max(0, elapsed.TotalSeconds) };
    }

    private bool IsAbandoned(PlaybackState state) => clock.GetUtcNow() - state.ReportedAt > StateLifetime;
}

public sealed class PlaybackHolder(string deviceId, string deviceName)
{
    private readonly TaskCompletionSource _displaced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string DeviceId { get; } = deviceId;
    public string DeviceName { get; } = deviceName;
    public PlaybackHolder? DisplacedBy { get; private set; }

    internal void Displace(PlaybackHolder by)
    {
        DisplacedBy = by;
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
