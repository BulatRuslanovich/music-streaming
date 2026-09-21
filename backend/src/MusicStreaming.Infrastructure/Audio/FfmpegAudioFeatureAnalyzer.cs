// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Audio;

public class FfmpegAudioFeatureAnalyzer(
    IOptions<TranscodeOptions> transcode,
    IOptions<AudioAnalysisOptions> analysis,
    ILogger<FfmpegAudioFeatureAnalyzer> logger) : IAudioFeatureAnalyzer
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Частота дискретизации и длина окна анализа — часть алгоритма, а не настройка: их смена
    /// обесценивает все уже посчитанные признаки ровно так же, как бамп
    /// <see cref="AudioAnalysisWorker.AlgorithmVersion"/>, и требует переанализа библиотеки.
    /// 8 кГц хватает всему, что извлекается: темп, тембр и спад лежат ниже 4 кГц.
    /// </summary>
    private const int SampleRateHz = 8000;

    private const int MaximumSeconds = 600;

    private readonly TranscodeOptions _transcode = transcode.Value;
    private readonly AudioAnalysisOptions _analysis = analysis.Value;
    private readonly ILogger<FfmpegAudioFeatureAnalyzer> _logger = logger;

    // Проба поднимает процесс, поэтому откладывается до первого обращения.
    private readonly Lazy<bool> _available = new(() => Probe(transcode.Value, logger));

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

        var maximumBytes = checked(SampleRateHz * MaximumSeconds * sizeof(float));
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
            FfmpegProcess.TryKill(process);
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

        return AudioFeatureExtraction.Extract(samples, SampleRateHz);
    }

    private ProcessStartInfo BuildStartInfo(string sourceAbsolutePath)
    {
        return FfmpegProcess.CreateStartInfo(
            _transcode.FfmpegPath,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error",
                "-i", sourceAbsolutePath,
                "-t", MaximumSeconds.ToString(),
                "-vn", "-map_metadata", "-1",
                "-ac", "1", "-ar", SampleRateHz.ToString(),
                "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1",
            ]);
    }

    private static bool Probe(TranscodeOptions settings, ILogger logger)
    {
        try
        {
            var startInfo = FfmpegProcess.CreateStartInfo(settings.FfmpegPath, ["-version"]);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ProbeTimeout))
            {
                FfmpegProcess.TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogInformation("Audio analysis is unavailable: {Reason}", ex.Message);
            return false;
        }
    }

}
