// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.Mvc;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Api.Controllers;

[ApiController]
[Route("api/connect")]
public class ConnectController(ConnectRegistry devices, ICurrentUser user, ConnectTrackService tracks) : ControllerBase
{
    [HttpPut("devices/{id}")]
    public ActionResult<ConnectPollDto> Poll(string id, ConnectHeartbeat heartbeat) =>
        Ok(devices.Poll(user.Id, id, heartbeat));

    [HttpPost("devices/{id}/commands")]
    public IActionResult Send(string id, ConnectCommandRequest request)
    {
        devices.Send(user.Id, id, request);
        return NoContent();
    }

    [HttpDelete("devices/{id}")]
    public IActionResult Remove(string id)
    {
        devices.Remove(user.Id, id);
        return NoContent();
    }

    [HttpPost("tracks")]
    public async Task<ActionResult<IReadOnlyList<TrackDto>>> Tracks(Guid[] ids, CancellationToken ct) =>
        Ok(await tracks.GetAsync(ids, ct));
}
