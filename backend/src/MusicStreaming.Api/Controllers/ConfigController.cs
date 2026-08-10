using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Api.Controllers;

/// <summary>
/// Server-side settings the player needs to behave correctly — chiefly the listening threshold
/// that decides when a play is recorded, so the value lives in one place instead of being
/// duplicated as a magic number in the frontend.
/// </summary>
[ApiController]
[Route("api/config")]
public sealed class ConfigController(
    IOptions<PlaybackOptions> playback,
    IOptions<StorageOptions> storage) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(new ClientConfigDto(
        playback.Value.HistoryThresholdSeconds,
        storage.Value.MaxUploadBytes));
}

public sealed record ClientConfigDto(int HistoryThresholdSeconds, long MaxUploadBytes);
