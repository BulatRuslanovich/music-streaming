// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text;
using Microsoft.Extensions.Options;
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

    public static OptionsBuilder<JwtOptions> Validated(OptionsBuilder<JwtOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey is required. Set JWT_SIGNING_KEY in .env, or use dotnet user-secrets for local development.")
        .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, $"Jwt:SigningKey must be at least 32 bytes. Generate one with: openssl rand -base64 48")
        .Validate(o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be greater than zero.")
        .Validate(o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be greater than zero.");
}

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "/storage";
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
    public long MaxImageUploadBytes { get; set; } = 8L * 1024 * 1024;

    public static OptionsBuilder<StorageOptions> Validated(OptionsBuilder<StorageOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.")
        .Validate(o => o.MaxUploadBytes > 0, "Storage:MaxUploadBytes must be greater than zero.")
        .Validate(o => o.MaxImageUploadBytes > 0, "Storage:MaxImageUploadBytes must be greater than zero.");
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

    public bool BackfillEnabled { get; set; } = true;

    public int BackfillBatchSize { get; set; } = 8;

    public int BackfillPauseSeconds { get; set; } = 5;

    public int BackfillStartupDelaySeconds { get; set; } = 30;

    public int? BitrateFor(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => LowBitrateKbps,
        AudioQuality.Normal => NormalBitrateKbps,
        AudioQuality.High => HighBitrateKbps,
        _ => null,
    };

    public static OptionsBuilder<TranscodeOptions> Validated(OptionsBuilder<TranscodeOptions> builder) => builder
        .Validate(
            o => o.LowBitrateKbps is >= 32 and <= 320
                 && o.NormalBitrateKbps is >= 32 and <= 320
                 && o.HighBitrateKbps is >= 32 and <= 320,
            "Transcode bitrates must be between 32 and 320.")
        .Validate(
            o => o.LowBitrateKbps <= o.NormalBitrateKbps && o.NormalBitrateKbps <= o.HighBitrateKbps,
            "Transcode bitrates must not decrease from Low to High.")
        .Validate(
            o => o.HlsSegmentSeconds is >= 2 and <= 10,
            "Transcode:HlsSegmentSeconds must be between 2 and 10.")
        .Validate(
            o => !string.IsNullOrWhiteSpace(o.FfmpegPath),
            "Transcode:FfmpegPath is required.")
        .Validate(
            o => o.BackfillBatchSize is >= 1 and <= 64,
            "Transcode:BackfillBatchSize must be between 1 and 64.")
        .Validate(
            o => o.BackfillPauseSeconds is >= 1 and <= 3600,
            "Transcode:BackfillPauseSeconds must be between 1 and 3600.")
        .Validate(
            o => o.BackfillStartupDelaySeconds is >= 0 and <= 3600,
            "Transcode:BackfillStartupDelaySeconds must be between 0 and 3600.");
}

public class AudioAnalysisOptions
{
    public const string SectionName = "AudioAnalysis";

    public bool Enabled { get; set; } = true;
    public int SampleRateHz { get; set; } = 8000;
    public int MaximumSeconds { get; set; } = 600;
    public int BackfillBatchSize { get; set; } = 4;
    public int PollSeconds { get; set; } = 30;

    public static OptionsBuilder<AudioAnalysisOptions> Validated(OptionsBuilder<AudioAnalysisOptions> builder) => builder
        .Validate(o => o.SampleRateHz is >= 4000 and <= 48000,
            "AudioAnalysis:SampleRateHz must be between 4000 and 48000.")
        .Validate(o => o.MaximumSeconds is >= 30 and <= 3600,
            "AudioAnalysis:MaximumSeconds must be between 30 and 3600.")
        .Validate(o => o.BackfillBatchSize is >= 1 and <= 64,
            "AudioAnalysis:BackfillBatchSize must be between 1 and 64.")
        .Validate(o => o.PollSeconds is >= 5 and <= 3600,
            "AudioAnalysis:PollSeconds must be between 5 and 3600.");
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
    public int RequestDelayMs { get; set; } = 500;
    public int DurationToleranceSeconds { get; set; } = 2;

    public static OptionsBuilder<LrclibOptions> Validated(OptionsBuilder<LrclibOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Lrclib:BaseUrl is required.")
        .Validate(o => o.RequestDelayMs >= 0, "Lrclib:RequestDelayMs cannot be negative.")
        .Validate(o => o.DurationToleranceSeconds >= 0, "Lrclib:DurationToleranceSeconds cannot be negative.");
}

public class AudioDbOptions
{
    public const string SectionName = "AudioDb";

    public string ApiKey { get; set; } = "2";
    public string BaseUrl { get; set; } = "https://www.theaudiodb.com/api/v1/json";
    public int RequestDelayMs { get; set; } = 1000;

    public static OptionsBuilder<AudioDbOptions> Validated(OptionsBuilder<AudioDbOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "AudioDb:ApiKey is required.")
        .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "AudioDb:BaseUrl is required.")
        .Validate(o => o.RequestDelayMs >= 0, "AudioDb:RequestDelayMs cannot be negative.");
}

public class LibraryEnrichmentOptions
{
    public const string SectionName = "LibraryEnrichment";

    public bool Enabled { get; set; } = true;
}

public class TagEnrichmentOptions
{
    public const string SectionName = "TagEnrichment";

    public bool Enabled { get; set; } = true;

    /// <summary>Сколько тегов сохраняется на артиста или трек.</summary>
    public int MaxTagsPerEntity { get; set; } = 12;

    /// <summary>Ниже этого веса тег не несёт информации и только раздувает вектор.</summary>
    public double MinimumTagWeight { get; set; } = 0.05;

    /// <summary>Сколько артистов и сколько треков дозагружается за один проход обслуживания.</summary>
    public int BackfillBatchSize { get; set; } = 50;

    /// <summary>Пауза между запросами к провайдеру, мс.</summary>
    public int RequestDelayMs { get; set; } = 350;

    /// <summary>Через сколько дней теги считаются устаревшими и запрашиваются заново.</summary>
    public int RefreshAfterDays { get; set; } = 180;

    public static OptionsBuilder<TagEnrichmentOptions> Validated(OptionsBuilder<TagEnrichmentOptions> builder) => builder
        .Validate(o => o.MaxTagsPerEntity > 0, "TagEnrichment:MaxTagsPerEntity must be positive.")
        .Validate(
            o => o.MinimumTagWeight is >= 0 and <= 1,
            "TagEnrichment:MinimumTagWeight must be between 0 and 1.")
        .Validate(o => o.BackfillBatchSize >= 0, "TagEnrichment:BackfillBatchSize cannot be negative.")
        .Validate(o => o.RequestDelayMs >= 0, "TagEnrichment:RequestDelayMs cannot be negative.")
        .Validate(o => o.RefreshAfterDays > 0, "TagEnrichment:RefreshAfterDays must be positive.");
}

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public int HistoryRetentionEntries { get; set; } = 1000;

    public static OptionsBuilder<PlaybackOptions> Validated(OptionsBuilder<PlaybackOptions> builder) => builder
        .Validate(o => o.HistoryThresholdSeconds > 0, "Playback:HistoryThresholdSeconds must be greater than zero.")
        .Validate(o => o.HistoryRetentionEntries > 0, "Playback:HistoryRetentionEntries must be greater than zero.");
}

public enum ImportDisposition
{
    Delete,
    Move,
}

public class LibraryImportOptions
{
    public const string SectionName = "LibraryImport";

    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "import";

    public int ScanIntervalSeconds { get; set; } = 300;

    public int StartupDelaySeconds { get; set; } = 20;

    public int BatchSize { get; set; } = 50;

    public int MinimumAgeSeconds { get; set; } = 15;

    public ImportDisposition AfterImport { get; set; } = ImportDisposition.Delete;

    public static OptionsBuilder<LibraryImportOptions> Validated(OptionsBuilder<LibraryImportOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.Directory), "LibraryImport:Directory is required.")
        .Validate(o => !Path.IsPathRooted(o.Directory), "LibraryImport:Directory must be relative to Storage:RootPath.")
        .Validate(o => o.ScanIntervalSeconds is >= 30 and <= 86400, "LibraryImport:ScanIntervalSeconds must be between 30 and 86400.")
        .Validate(o => o.StartupDelaySeconds is >= 0 and <= 3600, "LibraryImport:StartupDelaySeconds must be between 0 and 3600.")
        .Validate(o => o.BatchSize is >= 1 and <= 1000, "LibraryImport:BatchSize must be between 1 and 1000.")
        .Validate(o => o.MinimumAgeSeconds is >= 0 and <= 3600, "LibraryImport:MinimumAgeSeconds must be between 0 and 3600.");
}

public class SecurityOptions
{
    public const string SectionName = "Security";

    public int LoginAttemptsPerMinute { get; set; } = 10;

    public int UploadsPerMinute { get; set; } = 60;

    public int SearchesPerMinute { get; set; } = 120;

    public int EventsPerMinute { get; set; } = 120;

    public int AccountLockoutAttempts { get; set; } = 10;

    public int AccountLockoutMinutes { get; set; } = 15;

    public static OptionsBuilder<SecurityOptions> Validated(OptionsBuilder<SecurityOptions> builder) => builder
        .Validate(o => o.LoginAttemptsPerMinute > 0, "Security:LoginAttemptsPerMinute must be greater than zero.")
        .Validate(o => o.UploadsPerMinute > 0, "Security:UploadsPerMinute must be greater than zero.")
        .Validate(o => o.SearchesPerMinute > 0, "Security:SearchesPerMinute must be greater than zero.")
        .Validate(o => o.EventsPerMinute > 0, "Security:EventsPerMinute must be greater than zero.")
        .Validate(o => o.AccountLockoutAttempts >= 0, "Security:AccountLockoutAttempts cannot be negative.")
        .Validate(o => o.AccountLockoutMinutes > 0, "Security:AccountLockoutMinutes must be greater than zero.");
}
