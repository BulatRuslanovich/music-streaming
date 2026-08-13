namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Ключи внутрипроцессного кэша полок.
///
/// <para>
/// Общие для читателя, который его наполняет, и для генератора, который его сбрасывает: только что
/// перестроенные полки должны становиться видны сразу, а не после того, как запись случайно
/// протухнет.
/// </para>
/// </summary>
public static class RecommendationCacheKeys
{
    /// <summary>Ключ кэша набора полок конкретного пользователя.</summary>
    /// <param name="userId">Пользователь, для которого сформированы полки.</param>
    public static string Shelves(Guid userId) => $"recommendations:{userId}";
}
