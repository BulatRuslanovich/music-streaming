// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Audio;

public class FfmpegAudioTranscoder(
    IOptions<TranscodeOptions> options,
    ILogger<FfmpegAudioTranscoder> logger) : IAudioTranscoder
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly TranscodeOptions _options = options.Value;
    private readonly ILogger<FfmpegAudioTranscoder> _logger = logger;

    // Проба запускает процесс, поэтому откладывается до первого обращения — а не до первого
    // запроса на поток, как было бы, окажись она в конструкторе.
    private readonly Lazy<bool> _encoderPresent = new(() => ProbeEncoder(options.Value, logger));

    public bool IsAvailable => _options.Enabled && _encoderPresent.Value;

    public async Task<bool> TranscodeToOpusAsync(
        string sourceAbsolutePath,
        string targetAbsolutePath,
        int bitrateKbps,
        CancellationToken cancellationToken = default)
    {
        var temporaryPath = $"{targetAbsolutePath}.{Guid.CreateVersion7():N}.part";

        try
        {
            var exitCode = await RunAsync(
                [
                    "-nostdin", "-hide_banner", "-loglevel", "error",
                    "-i", sourceAbsolutePath,
                    "-vn",
                    "-map_metadata", "-1",
                    "-threads", "1",
                    "-c:a", "libopus",
                    "-b:a", $"{bitrateKbps}k",
                    "-vbr", "on",
                    "-application", "audio",
                    "-f", "ogg",
                    "-y", temporaryPath,
                ],
                cancellationToken);

            if (exitCode != 0 || !File.Exists(temporaryPath))
            {
                _logger.LogWarning(
                    "ffmpeg exited with {ExitCode} while encoding {Source}", exitCode, sourceAbsolutePath);
                return false;
            }

            File.Move(temporaryPath, targetAbsolutePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write the Opus rendition of {Source}", sourceAbsolutePath);
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public async Task<bool> TranscodeToHlsAsync(
        string sourceAbsolutePath,
        string targetDirectory,
        int bitrateKbps,
        CancellationToken cancellationToken = default)
    {
        var temporaryDirectory = $"{targetDirectory}.{Guid.CreateVersion7():N}.part";

        try
        {
            Directory.CreateDirectory(temporaryDirectory);

            var exitCode = await RunAsync(
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
                    "-hls_time", _options.HlsSegmentSeconds.ToString(),
                    "-hls_playlist_type", "vod",
                    "-hls_segment_type", "fmp4",
                    "-hls_flags", "independent_segments",
                    "-hls_fmp4_init_filename", "init.mp4",
                    "-hls_segment_filename", Path.Combine(temporaryDirectory, "segment-%05d.m4s"),
                    "-y", Path.Combine(temporaryDirectory, "index.m3u8"),
                ],
                cancellationToken);

            var ready = exitCode == 0
                        && File.Exists(Path.Combine(temporaryDirectory, "index.m3u8"))
                        && File.Exists(Path.Combine(temporaryDirectory, "init.mp4"))
                        && Directory.EnumerateFiles(temporaryDirectory, "segment-*.m4s").Any();

            if (!ready)
            {
                _logger.LogWarning(
                    "ffmpeg exited with {ExitCode} while preparing HLS for {Source}",
                    exitCode,
                    sourceAbsolutePath);
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
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    private static bool ProbeEncoder(TranscodeOptions settings, ILogger logger)
    {
        try
        {
            using var process = Process.Start(FfmpegProcess.CreateStartInfo(
                settings.FfmpegPath,
                ["-hide_banner", "-loglevel", "error", "-version"]));
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ProbeTimeout))
            {
                FfmpegProcess.TryKill(process);
                return false;
            }

            if (process.ExitCode == 0)
            {
                logger.LogInformation(
                    "Data-saver streams are available: {Ffmpeg} answered, renditions will be cached on demand",
                    settings.FfmpegPath);
                return true;
            }

            logger.LogWarning("{Ffmpeg} answered with exit code {ExitCode}", settings.FfmpegPath, process.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogInformation(
                "Data-saver streams are disabled: {Ffmpeg} could not be started ({Reason})",
                settings.FfmpegPath, ex.Message);
            return false;
        }
    }

    private async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(FfmpegProcess.CreateStartInfo(_options.FfmpegPath, arguments))
            ?? throw new InvalidOperationException($"{_options.FfmpegPath} could not be started.");

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            FfmpegProcess.TryKill(process);
            throw;
        }

        await Task.WhenAll(standardError, standardOutput);

        if (process.ExitCode != 0 && standardError.Result.Length > 0)
            _logger.LogDebug("ffmpeg: {Error}", standardError.Result.Trim());

        return process.ExitCode;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean up the partial transcode at {Path}", path);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean up the partial HLS rendition at {Path}", path);
        }
    }
}
