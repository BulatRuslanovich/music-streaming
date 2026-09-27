// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class TranscodeOptions
{
    public const string SectionName = "Transcode";

    public bool Enabled { get; set; } = true;

    public int HlsSegmentSeconds { get; set; } = 4;

    public string FfmpegPath { get; set; } = "ffmpeg";

    // ffmpeg здесь запускается с -threads 1, поэтому пропускную способность даёт число
    // параллельных заданий, а не потоков внутри одного. 0 — считать от машины: половина ядер,
    // чтобы остался запас на API. В контейнере ProcessorCount уже учитывает лимиты cgroup.
    public int Workers { get; set; }

    public int EffectiveWorkers => Workers > 0 ? Workers : Math.Max(1, Environment.ProcessorCount / 2);

    public bool BackfillEnabled { get; set; } = true;

    public int BackfillBatchSize { get; set; } = 8;

    public int BackfillPauseSeconds { get; set; } = 5;

    public static OptionsBuilder<TranscodeOptions> Validated(OptionsBuilder<TranscodeOptions> builder) => builder
        .Validate(
            o => o.HlsSegmentSeconds is >= 2 and <= 10,
            "Transcode:HlsSegmentSeconds must be between 2 and 10.")
        .Validate(
            o => !string.IsNullOrWhiteSpace(o.FfmpegPath),
            "Transcode:FfmpegPath is required.")
        .Validate(
            o => o.Workers is >= 0 and <= 32,
            "Transcode:Workers must be between 0 (auto) and 32.")
        .Validate(
            o => o.BackfillBatchSize is >= 1 and <= 64,
            "Transcode:BackfillBatchSize must be between 1 and 64.")
        .Validate(
            o => o.BackfillPauseSeconds is >= 1 and <= 3600,
            "Transcode:BackfillPauseSeconds must be between 1 and 3600.");
}
