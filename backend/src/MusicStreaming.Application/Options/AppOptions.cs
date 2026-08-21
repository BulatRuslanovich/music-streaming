// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Options;


public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "music-streaming";
    public string Audience { get; set; } = "music-streaming";
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 30;
}

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "/storage";
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
    public long MaxImageUploadBytes { get; set; } = 8L * 1024 * 1024;
}


public class TranscodeOptions
{
    public const string SectionName = "Transcode";

    public bool Enabled { get; set; } = true;

    public int LowBitrateKbps { get; set; } = 64;
    public int NormalBitrateKbps { get; set; } = 128;
    public int HighBitrateKbps { get; set; } = 192;

    public int HlsSegmentSeconds { get; set; } = 4;

    public string FfmpegPath { get; set; } = "ffmpeg";

    public int? BitrateFor(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => LowBitrateKbps,
        AudioQuality.Normal => NormalBitrateKbps,
        AudioQuality.High => HighBitrateKbps,
        _ => null,
    };
}

public class AudioAnalysisOptions
{
    public const string SectionName = "AudioAnalysis";

    public bool Enabled { get; set; } = true;
    public int SampleRateHz { get; set; } = 8000;
    public int MaximumSeconds { get; set; } = 600;
    public int BackfillBatchSize { get; set; } = 4;
    public int PollSeconds { get; set; } = 30;
}

public class LastfmOptions
{
    public const string SectionName = "Lastfm";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
}

public class LrclibOptions
{
    public const string SectionName = "Lrclib";

    public string BaseUrl { get; set; } = "https://lrclib.net";

    // LRCLIB держат бесплатно и без ключа, поэтому единственная вежливость, которую можно оказать —
    // не бить по нему очередью запросов без пауз.
    public int RequestDelayMs { get; set; } = 500;

    // Одноимённые записи у разных исполнителей и лайв-версии отличаются в первую очередь
    // длительностью, так что расхождение сверх этого допуска считается другим треком.
    public int DurationToleranceSeconds { get; set; } = 2;
}

public class AudioDbOptions
{
    public const string SectionName = "AudioDb";

    public string ApiKey { get; set; } = "2";
    public string BaseUrl { get; set; } = "https://www.theaudiodb.com/api/v1/json";
    public int RequestDelayMs { get; set; } = 1000;
}

public class LibraryEnrichmentOptions
{
    public const string SectionName = "LibraryEnrichment";

    public bool Enabled { get; set; } = true;
}

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public int HistoryRetentionEntries { get; set; } = 1000;
}
