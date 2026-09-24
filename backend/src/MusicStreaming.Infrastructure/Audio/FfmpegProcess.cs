// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MusicStreaming.Infrastructure.Audio;

internal static class FfmpegProcess
{
    public static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    /// <summary>Whether ffmpeg can be started at all.</summary>
    /// <remarks>
    /// Проба поднимает процесс, поэтому вызывающие держат её за <see cref="Lazy{T}"/> и платят
    /// один раз. Здесь она общая, потому что «есть ли ffmpeg» — вопрос не про конкретного
    /// потребителя: у транскодера и анализатора признаков ответ тот же, только каждый ещё
    /// накладывает поверх свой флаг включения.
    /// </remarks>
    public static bool IsPresent(string executable, ILogger logger)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(executable, ["-version"]));
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(ProbeTimeoutMs))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{Executable} is not usable; features that need it stay off", executable);
            return false;
        }
    }

    private const int ProbeTimeoutMs = 5000;

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
