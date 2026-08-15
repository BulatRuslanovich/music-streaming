using MusicStreaming.Domain.Common;

namespace MusicStreaming.Domain.Entities;

/// <summary>
/// Настройки одного слушателя, которые обязан знать сервер.
///
/// <para>
/// Отдельная сущность, а не колонки в <see cref="User"/>: <see cref="User"/> целиком грузится на
/// каждом входе и в каждой админской операции, а эти поля нужны только плееру и статистике.
/// Хранятся на сервере, а не в localStorage, потому что часовой пояс нужен агрегациям статистики,
/// а качество и автоплей должны переезжать вместе с пользователем на другое устройство.
/// </para>
/// </summary>
public class UserSettings
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Продолжать ли похожими треками, когда очередь закончилась.</summary>
    public bool Autoplay { get; set; } = true;

    /// <summary>Профиль качества, выбранный пользователем.</summary>
    public AudioQuality Quality { get; set; } = AudioQuality.Normal;

    /// <summary>Разовый переключатель «я на мобильном интернете»: пока включён, поток идёт в <see cref="AudioQuality.Low"/>, а выбранный профиль остаётся нетронутым.</summary>
    public bool DataSaver { get; set; }

    /// <summary>Часовой пояс IANA (например, <c>Europe/Moscow</c>), в котором считаются сутки и часы в статистике.</summary>
    public string TimeZone { get; set; } = "UTC";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Качество, с которым нужно отдавать поток прямо сейчас: экономия трафика перебивает выбранный профиль.</summary>
    public AudioQuality EffectiveQuality => DataSaver ? AudioQuality.Low : Quality;
}
