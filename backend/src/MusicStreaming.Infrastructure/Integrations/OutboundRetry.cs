using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Infrastructure.Integrations;

/// <summary>
/// Стоит ли повторять неудавшееся задание и когда.
///
/// <para>
/// Вынесено из воркера отдельно, потому что это единственное в нём настоящее решение, и оно должно
/// проверяться без базы, сети и фонового цикла.
/// </para>
/// </summary>
public static class OutboundRetry
{
    /// <summary>Выдержка перед очередной попыткой; после последней задание считается несостоявшимся.</summary>
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
    ];

    /// <summary>Пауза до следующей попытки или <c>null</c>, если повторять не нужно.</summary>
    /// <param name="kind">Вид задания.</param>
    /// <param name="attempts">Сколько попыток уже сделано, включая только что провалившуюся.</param>
    /// <param name="failure">Чем ответил внешний сервис.</param>
    public static TimeSpan? DelayFor(OutboundJobKind kind, int attempts, LastfmException failure)
    {
        // Отказ по существу (неверная подпись, отозванный доступ) повторять бессмысленно: ответ
        // будет тем же, а очередь встанет.
        if (!failure.IsTransient || failure.IsAuthFailure)
            return null;

        // «Сейчас играет» живёт минуты и к следующей попытке уже ничего не значит.
        if (kind == OutboundJobKind.LastfmNowPlaying)
            return null;

        return attempts >= 1 && attempts <= Backoff.Length ? Backoff[attempts - 1] : null;
    }
}
