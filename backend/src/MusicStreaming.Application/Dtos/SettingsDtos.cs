using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Dtos;

/// <param name="Autoplay">Продолжать ли похожими треками, когда очередь закончилась.</param>
/// <param name="Quality">Выбранная ступень качества.</param>
/// <param name="DataSaver">Временный переход на самую экономную ступень, не меняющий выбранную.</param>
/// <param name="TimeZone">Часовой пояс IANA, в котором считаются сутки и часы в статистике.</param>
public record UserSettingsDto(bool Autoplay, AudioQuality Quality, bool DataSaver, string TimeZone);

/// <summary>Частичное обновление: приходят только изменившиеся поля, остальные остаются как были.</summary>
public record UpdateUserSettingsRequest(
    bool? Autoplay,
    AudioQuality? Quality,
    bool? DataSaver,
    string? TimeZone);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
