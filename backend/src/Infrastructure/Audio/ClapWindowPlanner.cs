// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Infrastructure.Audio;

public static class ClapWindowPlanner
{
    public const double WindowSeconds = 10.0;

    public const string Strategy = "clap_3x10_v1";

    private const double MinimumSeparationSeconds = 0.5;

    private const double ShortTrackSlackSeconds = 0.05;

    public static IReadOnlyList<double> Plan(double durationSeconds)
    {
        if (durationSeconds <= 0)
            return [];

        if (durationSeconds <= WindowSeconds + ShortTrackSlackSeconds)
            return [0.0];

        var offsets = new List<double>(3);

        foreach (var candidate in (double[])
                 [0.0, (durationSeconds - WindowSeconds) / 2, durationSeconds - WindowSeconds])
        {
            var offset = Math.Max(0.0, candidate);

            if (offsets.TrueForAll(kept => Math.Abs(offset - kept) >= MinimumSeparationSeconds))
                offsets.Add(offset);
        }

        return offsets;
    }
}
