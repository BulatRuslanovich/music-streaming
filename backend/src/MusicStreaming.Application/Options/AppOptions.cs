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

    // ffmpeg здесь запускается с -threads 1, поэтому пропускную способность даёт число
    // параллельных заданий, а не потоков внутри одного. 0 — считать от машины: половина ядер,
    // чтобы остался запас на API. В контейнере ProcessorCount уже учитывает лимиты cgroup.
    public int Workers { get; set; }

    public int EffectiveWorkers => Workers > 0 ? Workers : Math.Max(1, Environment.ProcessorCount / 2);

    public bool BackfillEnabled { get; set; } = true;

    public int BackfillBatchSize { get; set; } = 8;

    public int BackfillPauseSeconds { get; set; } = 5;

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
            o => o.Workers is >= 0 and <= 32,
            "Transcode:Workers must be between 0 (auto) and 32.")
        .Validate(
            o => o.BackfillBatchSize is >= 1 and <= 64,
            "Transcode:BackfillBatchSize must be between 1 and 64.")
        .Validate(
            o => o.BackfillPauseSeconds is >= 1 and <= 3600,
            "Transcode:BackfillPauseSeconds must be between 1 and 3600.");
}

public class AudioAnalysisOptions
{
    public const string SectionName = "AudioAnalysis";

    /// <summary>
    /// Единственная настройка анализа. Всё остальное — частота дискретизации, длина окна, размер
    /// пачки, пауза — это части алгоритма: их правка требует переанализа библиотеки, поэтому они
    /// живут константами рядом с кодом, который их читает.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

public class AudioEmbeddingOptions
{
    public const string SectionName = "AudioEmbedding";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Чем считать вектора: <c>clap</c> — настоящей моделью, <c>deterministic</c> — дублем,
    /// раскладывающим путь файла в псевдослучайный вектор. Дубль нужен для локальной разработки
    /// без модели; его вектора о звуке ничего не знают, и в продакшене он бессмыслен.
    /// </summary>
    public string Provider { get; set; } = ClapProvider;

    public const string ClapProvider = "clap";
    public const string DeterministicProvider = "deterministic";

    /// <summary>Путь к .onnx относительно корня хранилища. Файл не в git: это сотни мегабайт.</summary>
    public string ModelPath { get; set; } = "models/clap/audio.onnx";

    /// <summary>
    /// Банк mel-фильтров, выгруженный вместе с моделью. Шкала Slaney не воспроизводится в коде
    /// намеренно: её ручная транскрипция была бы самым вероятным источником тихого расхождения.
    /// </summary>
    public string MelFiltersPath { get; set; } = "models/clap/mel_filters_64x513.f32";

    /// <summary>
    /// Ожидаемый SHA-256 модели. Пусто — проверка не выполняется; заполнено — несовпадение
    /// означает отказ загружать граф. Модель это исполняемый код, а не данные.
    /// </summary>
    public string ModelSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор модели. Вместе со стратегией нарезки играет роль версии алгоритма: смена
    /// любого из двух заставляет переэмбеддить всю библиотеку, а это часы или сутки CPU.
    /// Это решение, а не настройка по случаю.
    /// </summary>
    public string ModelId { get; set; } = "laion/larger_clap_music_and_speech";

    public int Dimension { get; set; } = 512;

    /// <summary>Сколько треков считается одновременно. Больше единицы конкурирует с транскодом.</summary>
    public int Workers { get; set; } = 1;

    /// <summary>Потоков внутри ORT; 0 — четверть ядер, чтобы стриминг не голодал.</summary>
    public int IntraOpThreads { get; set; }

    public int BackfillBatchSize { get; set; } = 4;
    public int PollSeconds { get; set; } = 30;

    public int EffectiveIntraOpThreads =>
        IntraOpThreads > 0 ? IntraOpThreads : Math.Max(1, Environment.ProcessorCount / 4);

    public static OptionsBuilder<AudioEmbeddingOptions> Validated(OptionsBuilder<AudioEmbeddingOptions> builder) => builder
        .Validate(o => o.Provider is ClapProvider or DeterministicProvider,
            $"AudioEmbedding:Provider must be '{ClapProvider}' or '{DeterministicProvider}'.")
        .Validate(o => o.Dimension is >= 32 and <= 4096,
            "AudioEmbedding:Dimension must be between 32 and 4096.")
        .Validate(o => o.Workers is >= 1 and <= 16,
            "AudioEmbedding:Workers must be between 1 and 16.")
        .Validate(o => o.IntraOpThreads is >= 0 and <= 128,
            "AudioEmbedding:IntraOpThreads must be between 0 and 128.")
        .Validate(o => o.BackfillBatchSize is >= 1 and <= 64,
            "AudioEmbedding:BackfillBatchSize must be between 1 and 64.")
        .Validate(o => o.PollSeconds is >= 5 and <= 3600,
            "AudioEmbedding:PollSeconds must be between 5 and 3600.")
        .Validate(o => o.ModelSha256.Length is 0 or 64,
            "AudioEmbedding:ModelSha256 must be empty or a 64-character hex digest.")
        .Validate(o => !Path.IsPathRooted(o.ModelPath) && !Path.IsPathRooted(o.MelFiltersPath),
            "AudioEmbedding paths must be relative to Storage:RootPath.");
}

public class LastfmOptions
{
    public const string SectionName = "Lastfm";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);

    // Секция целиком необязательна — обязательных полей здесь нет. Но полузаполненная секция
    // тише всего ломается уже в браузере, на редиректе Last.fm, поэтому она проверяется на старте.
    public static OptionsBuilder<LastfmOptions> Validated(OptionsBuilder<LastfmOptions> builder) => builder
        .Validate(
            o => string.IsNullOrWhiteSpace(o.ApiKey) == string.IsNullOrWhiteSpace(o.ApiSecret),
            "Lastfm:ApiKey and Lastfm:ApiSecret must be set together, or both left empty.")
        .Validate(
            o => string.IsNullOrWhiteSpace(o.PublicUrl)
                 || Uri.TryCreate(o.PublicUrl, UriKind.Absolute, out _),
            "Lastfm:PublicUrl must be an absolute URL — it is the origin of the OAuth callback.");
}

public class LrclibOptions
{
    public const string SectionName = "Lrclib";

    public string BaseUrl { get; set; } = "https://lrclib.net";
    public int RequestDelayMs { get; set; } = 500;

    public static OptionsBuilder<LrclibOptions> Validated(OptionsBuilder<LrclibOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Lrclib:BaseUrl is required.")
        .Validate(o => o.RequestDelayMs >= 0, "Lrclib:RequestDelayMs cannot be negative.");
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

    /// <summary>Пауза между запросами к провайдеру, мс.</summary>
    public int RequestDelayMs { get; set; } = 350;

    public static OptionsBuilder<TagEnrichmentOptions> Validated(OptionsBuilder<TagEnrichmentOptions> builder) => builder
        .Validate(o => o.RequestDelayMs >= 0, "TagEnrichment:RequestDelayMs cannot be negative.");
}

public class PlaybackOptions
{
    public const string SectionName = "Playback";

    public int HistoryThresholdSeconds { get; set; } = 30;

    public static OptionsBuilder<PlaybackOptions> Validated(OptionsBuilder<PlaybackOptions> builder) => builder
        .Validate(o => o.HistoryThresholdSeconds > 0, "Playback:HistoryThresholdSeconds must be greater than zero.");
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
