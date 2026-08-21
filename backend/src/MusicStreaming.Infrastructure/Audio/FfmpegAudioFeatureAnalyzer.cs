// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Audio;

public class FfmpegAudioFeatureAnalyzer : IAudioFeatureAnalyzer
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly TranscodeOptions _transcode;
    private readonly AudioAnalysisOptions _analysis;
    private readonly ILogger<FfmpegAudioFeatureAnalyzer> _logger;
    private readonly Lazy<bool> _available;

    public FfmpegAudioFeatureAnalyzer(
        IOptions<TranscodeOptions> transcode,
        IOptions<AudioAnalysisOptions> analysis,
        ILogger<FfmpegAudioFeatureAnalyzer> logger)
    {
        _transcode = transcode.Value;
        _analysis = analysis.Value;
        _logger = logger;
        _available = new Lazy<bool>(Probe);
    }

    public bool IsAvailable => _analysis.Enabled && _available.Value;

    public async Task<AudioFeatureVector?> AnalyzeAsync(
        string sourceAbsolutePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return null;

        using var process = Process.Start(BuildStartInfo(sourceAbsolutePath));
        if (process is null)
            return null;

        var maximumBytes = checked(_analysis.SampleRateHz * _analysis.MaximumSeconds * sizeof(float));
        using var pcm = new MemoryStream(Math.Min(maximumBytes, 8 * 1024 * 1024));
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(pcm, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await error;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0 || pcm.Length < sizeof(float))
        {
            _logger.LogWarning(
                "Audio analysis failed for {Source}: ffmpeg exited with {ExitCode} ({Error})",
                sourceAbsolutePath,
                process.ExitCode,
                error.Result.Trim());
            return null;
        }

        var bytes = pcm.ToArray();
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));

        return AudioFeatureExtraction.Extract(samples, _analysis.SampleRateHz);
    }

    private ProcessStartInfo BuildStartInfo(string sourceAbsolutePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _transcode.FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
                 {
                     "-nostdin", "-hide_banner", "-loglevel", "error",
                     "-i", sourceAbsolutePath,
                     "-t", _analysis.MaximumSeconds.ToString(),
                     "-vn", "-map_metadata", "-1",
                     "-ac", "1", "-ar", _analysis.SampleRateHz.ToString(),
                     "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private bool Probe()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _transcode.FfmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ProbeTimeout))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Audio analysis is unavailable: {Reason}", ex.Message);
            return false;
        }
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
}
