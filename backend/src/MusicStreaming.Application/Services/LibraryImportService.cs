// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Application.Services;

public class LibraryImportService(
    IImportSource source,
    TrackUploadService upload,
    LibraryImportState state,
    IOptions<LibraryImportOptions> options,
    ILogger<LibraryImportService> logger)
{
    public LibraryImportStatusDto Status(CancellationToken ct = default)
    {
        var settings = options.Value;
        var waiting = settings.Enabled ? source.Count(ct) : 0;

        return state.Snapshot(settings.Enabled, source.DisplayPath, waiting);
    }

    public async Task<LibraryImportStatusDto> ImportAsync(CancellationToken ct = default)
    {
        var settings = options.Value;

        if (!settings.Enabled)
            throw new ValidationException("Library import is disabled on this server.");

        if (!state.TryBegin())
        {
            logger.LogDebug("Import scan skipped: another scan is already running");
            return Status(ct);
        }

        try
        {
            await RunAsync(settings, ct);
        }
        finally
        {
            state.End();
        }

        return Status(ct);
    }

    private async Task RunAsync(LibraryImportOptions settings, CancellationToken ct)
    {
        var minimumAge = TimeSpan.FromSeconds(settings.MinimumAgeSeconds);
        var batch = source.Take(settings.BatchSize, minimumAge, ct);

        if (batch.Count == 0)
        {
            logger.LogDebug("Import scan found nothing to do in {Directory}", source.DisplayPath);
            return;
        }

        state.ReportPending(batch.Count);
        logger.LogInformation(
            "Importing {Count} files from {Directory}", batch.Count, source.DisplayPath);

        var imported = 0;
        var failed = 0;

        foreach (var file in batch)
        {
            ct.ThrowIfCancellationRequested();
            state.ReportStarted(file.RelativePath);

            var result = await upload.UploadAsync(
                new UploadCandidate(file.FileName, null, file.SizeBytes, () => source.OpenRead(file)),
                UploadOrigin.DirectoryImport,
                ct);

            if (result.Uploaded.Count > 0)
            {
                source.Consume(file);
                state.ReportImported();
                imported++;
                continue;
            }

            var reason = result.Failed.Count > 0
                ? result.Failed[0].Reason
                : "The file could not be imported.";

            source.Quarantine(file, reason);
            state.ReportFailed(new UploadFailureDto(file.RelativePath, reason));
            failed++;

            logger.LogWarning(
                "Import of {File} failed and the file was quarantined: {Reason}", file.RelativePath, reason);
        }

        logger.LogInformation(
            "Import run finished: {Imported} tracks added, {Failed} quarantined", imported, failed);
    }
}
