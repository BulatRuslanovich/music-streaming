namespace MusicStreaming.Application.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "music-streaming";
    public string Audience { get; set; } = "music-streaming";

    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}

public class StorageOptions
{
    public const string SectionName = "Storage";
    public string RootPath { get; set; } = "/storage";
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public long MaxImageUploadBytes { get; set; } = 8L * 1024 * 1024;
}

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public int HistoryRetentionEntries { get; set; } = 1000;
}
