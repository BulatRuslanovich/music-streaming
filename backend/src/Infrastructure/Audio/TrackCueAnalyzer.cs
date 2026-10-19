// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Infrastructure.Audio;

public class TrackCueAnalyzer(ILogger<TrackCueAnalyzer> logger)
{
    // Смена алгоритма в CueDetector — повод поднять версию: воркер сам пересчитает библиотеку.
    public const int Version = 1;

    private const int ReadBufferBytes = 64 * 1024;

    public async Task<TrackCuePoints?> AnalyzeAsync(string sourceAbsolutePath, CancellationToken ct = default)
    {
        using var process = Process.Start(FfmpegProcess.CreateStartInfo(
            FfmpegProcess.Executable,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error",
                "-i", sourceAbsolutePath,
                "-vn", "-map_metadata", "-1",
                "-ac", "1", "-ar", CueDetector.SampleRate.ToString(CultureInfo.InvariantCulture),
                "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1",
            ]));

        if (process is null)
            return null;

        var error = process.StandardError.ReadToEndAsync(ct);
        var detector = new CueDetector();

        try
        {
            var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[ReadBufferBytes];
            var carried = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(carried), ct);
                if (read == 0)
                    break;

                var available = carried + read;
                var whole = available / sizeof(float) * sizeof(float);

                detector.Feed(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, whole)));

                carried = available - whole;
                buffer.AsSpan(whole, carried).CopyTo(buffer);
            }

            await process.WaitForExitAsync(ct);
            await error;
        }
        catch (OperationCanceledException)
        {
            FfmpegProcess.TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            logger.LogWarning(
                "Cue analysis could not decode {Source}: ffmpeg exited with {ExitCode} ({Error})",
                sourceAbsolutePath,
                process.ExitCode,
                error.Result.Trim());

            return null;
        }

        return detector.Complete();
    }
}
