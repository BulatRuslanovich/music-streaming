// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

public record LibraryImportStatusDto(
    bool Enabled,
    string Directory,
    bool Running,
    int Waiting,
    int Pending,
    int Imported,
    int Failed,
    string? CurrentFile,
    IReadOnlyList<UploadFailureDto> RecentFailures);
