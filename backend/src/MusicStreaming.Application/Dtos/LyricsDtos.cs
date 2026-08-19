// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Dtos;

public record LyricLineDto(int At, string Text);

public record LyricsDto(
    Guid TrackId,
    string Plain,
    IReadOnlyList<LyricLineDto> Lines,
    LyricsSource Source);

public record UpdateLyricsRequest(string? Text);
