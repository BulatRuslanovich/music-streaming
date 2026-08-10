namespace MusicStreaming.Application.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "music-streaming";
    public string Audience { get; set; } = "music-streaming";

    /// <summary>HMAC signing key; must be at least 32 bytes and supplied via configuration.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Absolute path of the storage root that holds <c>music/</c> and <c>covers/</c>.</summary>
    public string RootPath { get; set; } = "/storage";

    /// <summary>Per-file upload ceiling. Defaults to 100 MB, comfortably above a long MP3.</summary>
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
}

public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    /// <summary>
    /// How many seconds of a track must be heard before it counts as played (spec default: 30).
    /// </summary>
    public int HistoryThresholdSeconds { get; set; } = 30;

    /// <summary>Upper bound on rows kept per user in the listening history.</summary>
    public int HistoryRetentionEntries { get; set; } = 1000;
}
