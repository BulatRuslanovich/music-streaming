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

    public string FfmpegPath { get; set; } = "ffmpeg";

    public int? BitrateFor(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => LowBitrateKbps,
        AudioQuality.Normal => NormalBitrateKbps,
        AudioQuality.High => HighBitrateKbps,
        _ => null,
    };
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

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public int HistoryRetentionEntries { get; set; } = 1000;
}
