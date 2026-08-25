// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

public interface IImportSource
{
    string DisplayPath { get; }
    int Count(CancellationToken cancellationToken = default);
    IReadOnlyList<ImportFile> Take(int limit, TimeSpan minimumAge, CancellationToken cancellationToken = default);
    Stream OpenRead(ImportFile file);
    void Consume(ImportFile file);
    void Quarantine(ImportFile file, string reason);
}

public record ImportFile(string RelativePath, string FileName, long SizeBytes, DateTimeOffset ModifiedAt);
