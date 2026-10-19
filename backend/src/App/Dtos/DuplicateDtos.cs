// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Dtos;

// Треки группы идут от лучшего качества к худшему: первый — тот, что стоит оставить.
public record DuplicateGroupDto(IReadOnlyList<TrackDto> Tracks, double Similarity);

public record DuplicateReportDto(
    IReadOnlyList<DuplicateGroupDto> Groups,
    int ComparedTracks,
    int TotalTracks);

public record DismissDuplicatesRequest(IReadOnlyList<Guid>? TrackIds);

public record MergeDuplicatesRequest(Guid KeepId, IReadOnlyList<Guid>? RemoveIds);
