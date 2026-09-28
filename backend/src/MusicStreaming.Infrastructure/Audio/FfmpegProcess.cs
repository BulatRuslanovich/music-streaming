// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MusicStreaming.Infrastructure.Audio;

internal static class FfmpegProcess
{
    /// <summary>ffmpeg is looked up on PATH: the runtime image installs it there, and so does a dev machine.</summary>
    public const string Executable = "ffmpeg";

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

    /// <summary>Whether ffmpeg can be started at all. Checked once, when the transcode worker starts.</summary>
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
            logger.LogError(exception, "{Executable} could not be started", executable);
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
