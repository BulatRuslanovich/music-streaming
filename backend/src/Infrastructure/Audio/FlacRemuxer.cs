// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;

namespace Infrastructure.Audio;

// FLAC в MP4-контейнере (так его отдают стриминги) → обычный .flac. Звук не перекодируется:
// ffmpeg переносит те же FLAC-фреймы в родной контейнер и сам пишет STREAMINFO и SEEKTABLE.
public class FlacRemuxer(ILogger<FlacRemuxer> logger)
{
    public async Task<bool> RemuxAsync(string sourcePath, string targetPath, CancellationToken ct)
    {
        using var process = Process.Start(FfmpegProcess.CreateStartInfo(
            FfmpegProcess.Executable,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error",
                "-i", sourcePath,
                "-map", "0:a:0",
                "-c:a", "copy",
                "-f", "flac",
                "-y", targetPath,
            ])) ?? throw new InvalidOperationException("ffmpeg could not be started.");

        var standardError = process.StandardError.ReadToEndAsync(ct);
        var standardOutput = process.StandardOutput.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            FfmpegProcess.TryKill(process);
            throw;
        }

        await Task.WhenAll(standardError, standardOutput);

        if (process.ExitCode == 0 && File.Exists(targetPath))
            return true;

        logger.LogWarning(
            "ffmpeg exited with {ExitCode} while unpacking FLAC from {Source}: {Error}",
            process.ExitCode,
            sourcePath,
            standardError.Result.Trim());
        return false;
    }
}
