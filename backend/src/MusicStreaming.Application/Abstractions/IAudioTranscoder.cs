namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// Перекодирование аудио в экономные ступени качества.
///
/// <para>
/// Внешняя программа, которой может не оказаться. Это не сбой конфигурации: без неё приложение
/// работает, просто отдаёт только исходники, и <c>ConfigController</c> не объявляет клиенту других
/// ступеней. Поэтому доступность — часть контракта, а не деталь реализации.
/// </para>
/// </summary>
public interface IAudioTranscoder
{
    /// <summary>Можно ли вообще перекодировать: включено настройкой и найден исполняемый файл.</summary>
    bool IsAvailable { get; }

    Task<bool> TranscodeToOpusAsync(
        string sourceAbsolutePath,
        string targetAbsolutePath,
        int bitrateKbps,
        CancellationToken cancellationToken = default);
}
