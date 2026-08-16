using System.Net.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Следит за тем, чтобы у одного аккаунта играло одно устройство.
/// </summary>
[ApiController]
[Route("api/playback")]
public class PlaybackController(
    PlaybackSessionRegistry sessions,
    ICurrentUser currentUser,
    ILogger<PlaybackController> logger) : ControllerBase
{
    /// <summary>
    /// Как часто в тихий поток уходит пустое событие.
    ///
    /// <para>
    /// Само приложение в них не нуждается — они нужны сети. Простаивающее соединение рвут и
    /// прокси, и мобильные операторы, а браузер узнаёт об этом не сразу; редкая строчка в потоке
    /// и держит его живым, и даёт клиенту заметить обрыв самому.
    /// </para>
    /// </summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Поток управляющих событий для играющего устройства.
    ///
    /// <para>
    /// Подписка и есть заявка: открыв поток, устройство объявляет, что играет оно, и прежнее
    /// получает <c>displaced</c> и замолкает. Отдельной ручки «захватить» нет намеренно — иначе
    /// между захватом и подпиской осталась бы щель, в которую проваливается ровно то событие,
    /// ради которого всё и затевалось.
    /// </para>
    ///
    /// <para>
    /// Клиент держит поток открытым, только пока играет: молчащему устройству сообщать нечего.
    /// </para>
    /// </summary>
    /// <param name="deviceId">Кто играет. Свой у каждой вкладки — две вкладки вытесняют друг друга.</param>
    [HttpGet("session")]
    public IResult Session([FromQuery] string? deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return TypedResults.BadRequest("A deviceId is required.");

        return TypedResults.ServerSentEvents(WatchAsync(currentUser.Id, deviceId, ct));
    }

    // CS8425: атрибута [EnumeratorCancellation] здесь намеренно нет. Токен приходит параметром
    // действия, то есть это HttpContext.RequestAborted — ровно то, что нужно; отдать управление
    // токеном перечислителю значило бы полагаться на то, какой токен передаст ему результат.
#pragma warning disable CS8425
    private async IAsyncEnumerable<SseItem<string>> WatchAsync(
        Guid userId, string deviceId, CancellationToken ct)
#pragma warning restore CS8425
    {
        var holder = sessions.Claim(userId, deviceId);

        logger.LogDebug("Device {DeviceId} took over playback for user {UserId}", deviceId, userId);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (await holder.WasDisplacedAsync(Heartbeat, ct))
                {
                    // Последнее, что уходит клиенту: дальше он сам закроет поток и встанет на паузу.
                    yield return new SseItem<string>(holder.DisplacedBy ?? string.Empty, "displaced");
                    yield break;
                }

                yield return new SseItem<string>(string.Empty, "ping");
            }
        }
        finally
        {
            sessions.Release(userId, holder);
            logger.LogDebug("Device {DeviceId} stopped holding playback for user {UserId}", deviceId, userId);
        }
    }
}
