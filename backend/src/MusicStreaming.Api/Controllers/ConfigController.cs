using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Api.Controllers;


[ApiController]
[Route("api/config")]
public class ConfigController(
    IOptions<PlaybackOptions> playback,
    IOptions<StorageOptions> storage,
    IAudioTranscoder transcoder) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(new ClientConfigDto(
        playback.Value.HistoryThresholdSeconds,
        storage.Value.MaxUploadBytes,
        transcoder.IsAvailable));
}

public record ClientConfigDto(int HistoryThresholdSeconds, long MaxUploadBytes, bool DataSaverAvailable);
