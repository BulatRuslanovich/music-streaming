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
    public static string Shelves(Guid userId) => $"recommendations:{userId}";
}
