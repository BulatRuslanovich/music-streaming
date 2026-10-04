// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using App.Services;

namespace Infrastructure.Audio;

public class FfmpegAudioTranscoder(ILogger<FfmpegAudioTranscoder> logger)
{
    private const int HlsSegmentSeconds = 4;

    private readonly ILogger<FfmpegAudioTranscoder> _logger = logger;

    public async Task<bool> TranscodeToHlsAsync(
        string sourceAbsolutePath,
        string targetDirectory,
        int bitrateKbps,
        CancellationToken ct = default)
    {
        var temporaryDirectory = $"{targetDirectory}.{Guid.CreateVersion7():N}.part";

        try
        {
            Directory.CreateDirectory(temporaryDirectory);

            using var process = Process.Start(FfmpegProcess.CreateStartInfo(
                FfmpegProcess.Executable,
                [
                    "-nostdin", "-hide_banner", "-loglevel", "error",
                    "-i", sourceAbsolutePath,
                    "-vn",
                    "-map_metadata", "-1",
                    "-map", "0:a:0",
                    "-threads", "1",
                    "-c:a", "aac",
                    "-profile:a", "aac_low",
                    "-b:a", $"{bitrateKbps}k",
                    "-ac", "2",
                    "-ar", "48000",
                    "-f", "hls",
                    "-hls_time", HlsSegmentSeconds.ToString(),
                    "-hls_playlist_type", "vod",
                    "-hls_segment_type", "fmp4",
                    "-hls_flags", "independent_segments+single_file",
                    "-hls_segment_filename", Path.Combine(temporaryDirectory, HlsPlaylist.MediaFileName),
                    "-y", Path.Combine(temporaryDirectory, HlsPlaylist.IndexFileName),
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

            var exitCode = process.ExitCode;
            var ready = exitCode == 0
                        && File.Exists(Path.Combine(temporaryDirectory, HlsPlaylist.IndexFileName))
                        && File.Exists(Path.Combine(temporaryDirectory, HlsPlaylist.MediaFileName));

            if (!ready)
            {
                _logger.LogWarning(
                    "ffmpeg exited with {ExitCode} while preparing HLS for {Source}: {Error}",
                    exitCode,
                    sourceAbsolutePath,
                    standardError.Result.Trim());
                return false;
            }

            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);

            Directory.Move(temporaryDirectory, targetDirectory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write the HLS rendition of {Source}", sourceAbsolutePath);
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not clean up the partial HLS rendition at {Path}", temporaryDirectory);
            }
        }
    }
}
