using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Options;

/// <summary>
/// Выпуск и проверка токенов. Ключ подписи обязателен и проверяется при старте: он должен быть не
/// короче 32 байт и не входить в список публично известных.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "music-streaming";
    public string Audience { get; set; } = "music-streaming";

    /// <summary>Секрет подписи HS256. Его смена разом обесценивает все выданные токены — все выходят из системы.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Срок жизни токена доступа. Короткий намеренно: отозвать выданный токен нельзя, поэтому
    /// именно это число задаёт, насколько быстро вступают в силу снятие прав и деактивация.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 10;

    /// <summary>Сколько можно не вводить пароль.</summary>
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>Где лежат файлы и какого размера их принимать.</summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Корень хранилища: музыка, обложки, кэш перекодирования и ключи шифрования. В Docker — примонтированный том.</summary>
    public string RootPath { get; set; } = "/storage";

    /// <summary>
    /// Потолок аудиофайла.
    ///
    /// <para>
    /// Это внутренний рубеж из трёх. Снаружи стоят граница тела запроса на обратном прокси и лимит
    /// Kestrel, и оба должны быть выше: тело всегда чуть больше самого файла из-за служебных байт
    /// multipart. Подняв это число и забыв про <c>MAX_UPLOAD_BODY_BYTES</c>, вы получите отказ
    /// прокси вместо понятного ответа API.
    /// </para>
    /// </summary>
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Потолок изображения: обложки альбомов и плейлистов, фотографии исполнителей.</summary>
    public long MaxImageUploadBytes { get; set; } = 8L * 1024 * 1024;
}

/// <summary>
/// Перекодирование в экономные ступени. Выключение оставляет доступным только исходник — клиент
/// узнаёт об этом из <c>/api/config</c> и показывает один вариант вместо четырёх.
/// </summary>
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

/// <summary>Ключи приложения Last.fm. Без них интеграция не предлагается пользователю вовсе.</summary>
public class LastfmOptions
{
    public const string SectionName = "Lastfm";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
}

/// <summary>Правила учёта прослушиваний.</summary>
public class PlaybackOptions
{
    public const string SectionName = "Playback";

    /// <summary>
    /// Сколько нужно прослушать, чтобы трек попал в историю. Это порог самого приложения; у Last.fm
    /// своё правило (половина трека или четыре минуты), и подменять его этим числом нельзя — иначе
    /// в чужом профиле окажется вдвое больше прослушиваний, чем показал бы любой другой плеер.
    /// </summary>
    public int HistoryThresholdSeconds { get; set; } = 30;

    /// <summary>Сколько записей истории хранится на пользователя; старые подрезаются.</summary>
    public int HistoryRetentionEntries { get; set; } = 1000;
}
