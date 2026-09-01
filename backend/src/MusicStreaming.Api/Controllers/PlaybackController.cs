// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/playback")]
public class PlaybackController(
    PlaybackSessionRegistry sessions,
    ICurrentUser currentUser) : ControllerBase
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    [HttpGet("session")]
    public IResult Session([FromQuery] string? deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ValidationException("A deviceId is required.");

        return TypedResults.ServerSentEvents(WatchAsync(currentUser.Id, deviceId, ct));
    }

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
