using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Api.Controllers;


[ApiController]
[Route("api/config")]
public class ConfigController(
    IOptions<PlaybackOptions> playback,
    IOptions<StorageOptions> storage) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(new ClientConfigDto(
        playback.Value.HistoryThresholdSeconds,
        storage.Value.MaxUploadBytes));
}

public record ClientConfigDto(int HistoryThresholdSeconds, long MaxUploadBytes);
