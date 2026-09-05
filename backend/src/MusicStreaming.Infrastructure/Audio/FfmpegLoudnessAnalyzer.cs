// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Audio;

public sealed class FfmpegLoudnessAnalyzer(
    IMusicStorage storage, IOptions<TranscodeOptions> options, ILogger<FfmpegLoudnessAnalyzer> logger) : ILoudnessAnalyzer, IDisposable
{
    // Анализ всей записи дорогой: один процесс на сервер, результаты переживают перезапуск.
    private readonly SemaphoreSlim gate = new(1);

    public async Task<LoudnessMeasurement?> GetAsync(string filePath, string contentHash, CancellationToken ct)
    {
        var cachePath = $"loudness/v1/{contentHash}.json";
        if (await ReadAsync(cachePath, ct) is { } saved) return saved;
        await gate.WaitAsync(ct);
        try
        {
            if (await ReadAsync(cachePath, ct) is { } cached) return cached;
            var source = storage.ResolveExisting(filePath);
            if (source is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var process = Process.Start(FfmpegProcess.CreateStartInfo(options.Value.FfmpegPath,
                ["-nostdin", "-hide_banner", "-nostats", "-i", source, "-map", "0:a:0", "-vn",
                    "-af", "loudnorm=I=-16:TP=-2:print_format=json", "-f", "null", "-"]));
            if (process is null) return null;
            string output;
            try
            {
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                output = await error;
                await stdout;
            }
            catch { FfmpegProcess.TryKill(process); throw; }
            if (process.ExitCode != 0) return null;
            var begin = output.LastIndexOf('{');
            var end = output.LastIndexOf('}');
            if (begin < 0 || end < begin) return null;
            using var json = JsonDocument.Parse(output[begin..(end + 1)]);
            var loudness = double.Parse(json.RootElement.GetProperty("input_i").GetString()!, CultureInfo.InvariantCulture);
            var peak = double.Parse(json.RootElement.GetProperty("input_tp").GetString()!, CultureInfo.InvariantCulture);
            if (!double.IsFinite(loudness) || !double.IsFinite(peak)) return null;
            var result = new LoudnessMeasurement(loudness, peak);
            var path = storage.ResolveForWrite(cachePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(result), ct);
            File.Move(path + ".tmp", path, true);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Loudness measurement unavailable for {Hash}", contentHash);
            return null;
        }
        finally { gate.Release(); }
    }

    private async Task<LoudnessMeasurement?> ReadAsync(string path, CancellationToken ct)
    {
        using var stream = storage.OpenRead(path);
        if (stream is null) return null;
        try { return await JsonSerializer.DeserializeAsync<LoudnessMeasurement>(stream, cancellationToken: ct); }
        catch (JsonException) { return null; }
    }

    public void Dispose() => gate.Dispose();
}
