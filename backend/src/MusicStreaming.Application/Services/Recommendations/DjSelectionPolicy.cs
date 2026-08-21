// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services.Recommendations;

internal static class DjSelectionPolicy
{
    public static double ExplorationRatio(DjVariety variety) => variety switch
    {
        DjVariety.Familiar => 0.10,
        DjVariety.Balanced => 0.35,
        DjVariety.Adventurous => 0.70,
        _ => throw new ValidationException("Unknown DJ variety."),
    };
}
