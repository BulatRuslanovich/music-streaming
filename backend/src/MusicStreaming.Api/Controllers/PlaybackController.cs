// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Как часто в тихий поток уходит пустое событие.
    /// </summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Поток управляющих событий для играющего устройства.
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

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (await holder.WasDisplacedAsync(Heartbeat, ct))
                {
                    yield return new SseItem<string>(holder.DisplacedBy ?? string.Empty, "displaced");
                    yield break;
                }

                yield return new SseItem<string>(string.Empty, "ping");
            }
        }
        finally
        {
            sessions.Release(userId, holder);
        }
    }
}
