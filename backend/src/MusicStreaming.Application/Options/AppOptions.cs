using MusicStreaming.Domain.Common;

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
    /// <summary>
    /// Потолок одного аудиофайла. С запасом на форматы без потерь: пять минут FLAC 16/44.1 — это
    /// уже около тридцати мегабайт, а 24/96 переваливает за сотню.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
    public long MaxImageUploadBytes { get; set; } = 8L * 1024 * 1024;
}

/// <summary>
/// Лестница качества. Все перекодированные ступени — Opus в контейнере Ogg: один кодек на все
/// ступени означает один путь в ffmpeg и одно поведение при воспроизведении, а по качеству на бит
/// Opus не уступает ничему из того, что умеет собрать образ. Исходный файл (mp3) остаётся четвёртой
/// ступенью и играет там, где Opus не поддержан.
/// </summary>
public class TranscodeOptions
{
    public const string SectionName = "Transcode";

    public bool Enabled { get; set; } = true;

    public int LowBitrateKbps { get; set; } = 64;
    public int NormalBitrateKbps { get; set; } = 128;
    public int HighBitrateKbps { get; set; } = 192;

    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Битрейт ступени или <c>null</c> для <see cref="AudioQuality.Original"/>, которую не перекодируют.</summary>
    public int? BitrateFor(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => LowBitrateKbps,
        AudioQuality.Normal => NormalBitrateKbps,
        AudioQuality.High => HighBitrateKbps,
        _ => null,
    };
}

/// <summary>
/// Доступ приложения к Last.fm. Ключ и секрет заводит владелец установки в своём кабинете
/// разработчика Last.fm; пока их нет, интеграция не предлагается и ничего не отправляет.
/// </summary>
public class LastfmOptions
{
    public const string SectionName = "Lastfm";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
}

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public int HistoryRetentionEntries { get; set; } = 1000;
}
