// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Common;
using App.Dtos;
using Infrastructure.Persistence;

namespace App.Services;

public class PlaybackHandoffService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    PlaybackSessionRegistry sessions,
    TimeProvider clock)
{
    private const int KeptBehind = 100;
    private const int KeptAhead = 400;
    private const int DeviceNameLength = 64;

    private static readonly string[] RepeatModes = ["off", "all", "one"];

    public void Report(PlaybackStateReport report)
    {
        var deviceId = RequireDevice(report.DeviceId);
        var trackIds = report.TrackIds ?? [];

        if (report.Index < 0 || report.Index >= trackIds.Count)
            throw new ValidationException("The index must point into the queue.");

        var repeat = report.Repeat ?? "off";
        if (!RepeatModes.Contains(repeat))
            throw new ValidationException("Repeat must be off, all or one.");

        var from = Math.Max(0, report.Index - KeptBehind);
        var kept = trackIds.Skip(from).Take(report.Index - from + 1 + KeptAhead).ToList();

        var accepted = sessions.Report(currentUser.Id, new PlaybackState(
            deviceId,
            DeviceName(report.DeviceName),
            kept,
            report.Index - from,
            Math.Max(0, report.PositionSeconds),
            report.IsPlaying,
            report.Shuffle,
            repeat,
            clock.GetUtcNow()));

        if (!accepted)
            throw new ConflictException("Another device holds playback.");
    }

    public async Task<PlayingElsewhereDto?> ElsewhereAsync(string? deviceId, CancellationToken ct)
    {
        var state = sessions.Elsewhere(currentUser.Id, RequireDevice(deviceId));
        if (state is null)
            return null;

        var queue = await RestoreAsync(state, ct);
        if (queue is null)
            return null;

        return new PlayingElsewhereDto(
            state.DeviceId, state.DeviceName, queue.Tracks[queue.Index], queue.PositionSeconds, state.IsPlaying,
            state.ReportedAt);
    }

    public async Task<PlaybackHandoffDto> HandoffAsync(PlaybackHandoffRequest request, CancellationToken ct)
    {
        var state = sessions.Elsewhere(currentUser.Id, RequireDevice(request.DeviceId))
            ?? throw new NotFoundException("Nothing is playing on another device.");

        var queue = await RestoreAsync(state, ct)
            ?? throw new NotFoundException("The tracks that were playing are gone.");

        return new PlaybackHandoffDto(
            state.DeviceId, state.DeviceName, queue.Tracks, queue.Index, queue.PositionSeconds, state.Shuffle,
            state.Repeat);
    }

    private async Task<RestoredQueue?> RestoreAsync(PlaybackState state, CancellationToken ct)
    {
        var known = await db.TracksByIdAsync(currentUser.Id, state.TrackIds, ct);

        var tracks = new List<TrackDto>(state.TrackIds.Count);
        var index = -1;
        var position = state.PositionSeconds;

        for (var i = 0; i < state.TrackIds.Count; i++)
        {
            if (!known.TryGetValue(state.TrackIds[i], out var track))
                continue;

            if (index < 0 && i >= state.Index)
            {
                index = tracks.Count;
                position = i == state.Index ? Math.Min(position, track.DurationSeconds) : 0;
            }

            tracks.Add(track);
        }

        return index < 0 ? null : new RestoredQueue(tracks, index, position);
    }

    private static string RequireDevice(string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? throw new ValidationException("A deviceId is required.") : deviceId;

    private static string DeviceName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length > DeviceNameLength ? trimmed[..DeviceNameLength] : trimmed;
    }

    private sealed record RestoredQueue(IReadOnlyList<TrackDto> Tracks, int Index, double PositionSeconds);
}
