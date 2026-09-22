// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Dtos;

public record UploadResultDto(
    IReadOnlyList<TrackDto> Uploaded,
    IReadOnlyList<UploadFailureDto> Failed);

public record UploadFailureDto(string FileName, string Reason);

public record BulkDeleteTracksRequest(IReadOnlyList<Guid>? Ids);

public record BulkDeleteResultDto(int Deleted, IReadOnlyList<Guid> Missing);

public record UploadProbeFileDto(
    string FileName,
    string? ContentHash,
    string? Title,
    string? Artist);

public record UploadProbeRequest(IReadOnlyList<UploadProbeFileDto> Files);

public enum UploadProbeVerdict
{
    New,
    Duplicate,
    Similar,
}

public enum UploadProbeBasis
{
    None,
    Tags,
    Hash,
    HashAndTags,
}

public record UploadProbeMatchDto(
    string FileName,
    UploadProbeVerdict Verdict,
    UploadProbeBasis Basis,
    TrackDto? Match);

public record UploadProbeResultDto(IReadOnlyList<UploadProbeMatchDto> Files);
