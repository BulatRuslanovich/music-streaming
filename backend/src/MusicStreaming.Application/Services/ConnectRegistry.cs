// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

public sealed class ConnectRegistry(TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<(Guid User, string Device), Device> devices = [];
    private static readonly TimeSpan Presence = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CommandLife = TimeSpan.FromSeconds(10);

    public ConnectPollDto Poll(Guid user, string id, ConnectHeartbeat heartbeat)
    {
        ValidateId(id);
        ValidateState(heartbeat.State);
        if (string.IsNullOrWhiteSpace(heartbeat.Name) || heartbeat.Name.Length > 100 ||
            heartbeat.Acknowledged is null || heartbeat.Acknowledged.Length > 32)
            throw new ValidationException("Invalid device heartbeat.");
        lock (gate)
        {
            Prune();
            if (!devices.TryGetValue((user, id), out var device))
            {
                if (devices.Keys.Count(key => key.User == user) >= 32)
                    throw new ConflictException("Too many connected devices.");
                device = new Device();
                devices[(user, id)] = device;
            }
            device.Name = heartbeat.Name.Trim();
            device.State = heartbeat.State;
            device.UpdatedAt = clock.GetUtcNow();
            device.Commands.RemoveAll(command => heartbeat.Acknowledged.Contains(command.Id));
            return new ConnectPollDto([.. devices.Where(pair => pair.Key.User == user)
                .Select(pair => new ConnectDeviceDto(pair.Key.Device, pair.Value.Name,
                    pair.Value.State!.Position, pair.Value.State.IsPlaying, pair.Value.State.Volume,
                    pair.Value.State.Muted, pair.Value.State.Title, pair.Value.UpdatedAt))],
                device.Commands.ToArray(), clock.GetUtcNow());
        }
    }

    public void Send(Guid user, string id, ConnectCommandRequest request)
    {
        ValidateId(id);
        if (request.Kind is not ("play" or "pause" or "next" or "previous" or "seek" or "volume" or "transfer"))
            throw new ValidationException("Unknown playback command.");
        if (request.Kind is "seek" or "volume" &&
            (request.Value is not { } value || !double.IsFinite(value) || value < 0 ||
             value > (request.Kind == "volume" ? 1 : 86400)))
            throw new ValidationException("Invalid command value.");
        lock (gate)
        {
            Prune();
            if (!devices.TryGetValue((user, id), out var target))
                throw new NotFoundException("Device is no longer connected.");
            ConnectState? state = null;
            if (request.Kind == "transfer")
            {
                if (request.SourceDeviceId is null || request.SourceDeviceId == id ||
                    !devices.TryGetValue((user, request.SourceDeviceId), out var source) ||
                    source.State is not { Queue.Length: > 0 } snapshot || snapshot.Index < 0)
                    throw new ValidationException("The source device has no playback to transfer.");
                var elapsed = snapshot.IsPlaying ? Math.Clamp((clock.GetUtcNow() - source.UpdatedAt).TotalSeconds, 0, 5) : 0;
                state = snapshot with { Position = snapshot.Position + elapsed };
            }
            if (target.Commands.Count >= 32) throw new ConflictException("Device is busy. Try again shortly.");
            target.Commands.Add(new ConnectCommandDto(Guid.CreateVersion7(), request.Kind, request.Value,
                state, clock.GetUtcNow() + CommandLife));
        }
    }

    public void Remove(Guid user, string id)
    {
        lock (gate) devices.Remove((user, id));
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var key in devices.Where(pair => now - pair.Value.UpdatedAt >= Presence).Select(pair => pair.Key).ToArray())
            devices.Remove(key);
        foreach (var device in devices.Values) device.Commands.RemoveAll(command => command.ExpiresAt <= now);
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) throw new ValidationException("Invalid device ID.");
    }

    private static void ValidateState(ConnectState? state)
    {
        if (state is null || state.Queue is null || state.Order is null || state.Queue.Length > 5000 ||
            state.Order.Length != state.Queue.Length || state.Order.Distinct().Count() != state.Order.Length ||
            state.Order.Any(index => index < 0 || index >= state.Queue.Length) ||
            state.Index < -1 || state.Index >= state.Queue.Length ||
            !double.IsFinite(state.Position) || state.Position < 0 || state.Position > 86400 ||
            !double.IsFinite(state.Volume) || state.Volume < 0 || state.Volume > 1 ||
            state.Repeat is not ("off" or "one" or "all") || state.Title?.Length > 500)
            throw new ValidationException("Invalid playback snapshot.");
    }

    private sealed class Device
    {
        public string Name { get; set; } = "";
        public ConnectState? State { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public List<ConnectCommandDto> Commands { get; } = [];
    }
}
