// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

public record ConnectState(Guid[] Queue, int[] Order, int Index, double Position,
    bool IsPlaying, double Volume, bool Muted, bool Shuffle, string Repeat, string? Title);
public record ConnectHeartbeat(string Name, ConnectState State, Guid[] Acknowledged);
public record ConnectDeviceDto(string Id, string Name, double Position, bool IsPlaying,
    double Volume, bool Muted, string? Title, DateTimeOffset UpdatedAt);
public record ConnectCommandRequest(string Kind, double? Value = null, string? SourceDeviceId = null);
public record ConnectCommandDto(Guid Id, string Kind, double? Value, ConnectState? State,
    DateTimeOffset ExpiresAt);
public record ConnectPollDto(IReadOnlyList<ConnectDeviceDto> Devices, IReadOnlyList<ConnectCommandDto> Commands,
    DateTimeOffset ServerTime);
