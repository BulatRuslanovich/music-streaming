// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using App.Abstractions;
using App.Common;
using App.Dtos;
using App.Services;

namespace Api.Controllers;

[ApiController]
[Route("api/playback")]
public class PlaybackController(
    PlaybackSessionRegistry sessions,
    PlaybackHandoffService handoff,
    ICurrentUser currentUser) : ControllerBase
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    [HttpPut("state")]
    public IActionResult Report(PlaybackStateReport report)
    {
        handoff.Report(report);
        return NoContent();
    }

    [HttpGet("now")]
    public async Task<ActionResult<PlayingElsewhereDto>> Now([FromQuery] string? deviceId, CancellationToken ct) =>
        await handoff.ElsewhereAsync(deviceId, ct) is { } elsewhere ? Ok(elsewhere) : NoContent();

    [HttpPost("handoff")]
    public async Task<ActionResult<PlaybackHandoffDto>> Handoff(PlaybackHandoffRequest request, CancellationToken ct) =>
        Ok(await handoff.HandoffAsync(request, ct));

    [HttpGet("session")]
    public IResult Session([FromQuery] string? deviceId, [FromQuery] string? deviceName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ValidationException("A deviceId is required.");

        return TypedResults.ServerSentEvents(WatchAsync(currentUser.Id, deviceId, deviceName ?? string.Empty, ct));
    }

#pragma warning disable CS8425
    private async IAsyncEnumerable<SseItem<string>> WatchAsync(
        Guid userId, string deviceId, string deviceName, CancellationToken ct)
#pragma warning restore CS8425
    {
        var holder = sessions.Claim(userId, deviceId, deviceName);

        try
        {
            yield return new SseItem<string>(deviceId, "claimed");

            while (!ct.IsCancellationRequested)
            {
                if (await holder.WasDisplacedAsync(Heartbeat, ct))
                {
                    var by = holder.DisplacedBy;
                    var takeover = new PlaybackTakeoverDto(by?.DeviceId ?? string.Empty, by?.DeviceName ?? string.Empty);

                    yield return new SseItem<string>(JsonSerializer.Serialize(takeover, JsonSerializerOptions.Web), "displaced");
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
