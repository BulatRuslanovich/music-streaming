using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Audio;

public class FfmpegAudioTranscoder : IAudioTranscoder
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly TranscodeOptions _options;
    private readonly ILogger<FfmpegAudioTranscoder> _logger;
    private readonly Lazy<bool> _encoderPresent;

    public FfmpegAudioTranscoder(IOptions<TranscodeOptions> options, ILogger<FfmpegAudioTranscoder> logger)
    {
        _options = options.Value;
        _logger = logger;
        _encoderPresent = new Lazy<bool>(ProbeEncoder);
    }

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

    private bool ProbeEncoder()
    {
        try
        {
            using var process = Process.Start(BuildStartInfo(["-hide_banner", "-loglevel", "error", "-version"]));
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ProbeTimeout))
            {
                TryKill(process);
                return false;
            }

            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "Data-saver streams are available: {Ffmpeg} answered, renditions will be cached on demand",
                    _options.FfmpegPath);
                return true;
            }

            _logger.LogWarning("{Ffmpeg} answered with exit code {ExitCode}", _options.FfmpegPath, process.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(
                "Data-saver streams are disabled: {Ffmpeg} could not be started ({Reason})",
                _options.FfmpegPath, ex.Message);
            return false;
        }
    }

    private async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(BuildStartInfo(arguments))
            ?? throw new InvalidOperationException($"{_options.FfmpegPath} could not be started.");

        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await Task.WhenAll(standardError, standardOutput);

        if (process.ExitCode != 0 && standardError.Result.Length > 0)
            _logger.LogDebug("ffmpeg: {Error}", standardError.Result.Trim());

        return process.ExitCode;
    }

    private ProcessStartInfo BuildStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
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
}
