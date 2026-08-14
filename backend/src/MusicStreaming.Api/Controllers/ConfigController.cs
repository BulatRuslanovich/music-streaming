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
    IOptions<TranscodeOptions> transcode,
    IAudioTranscoder transcoder) : ControllerBase
{
    [HttpGet]
    public ActionResult<ClientConfigDto> Get() => Ok(new ClientConfigDto(
        playback.Value.HistoryThresholdSeconds,
        playback.Value.HistoryRetentionEntries,
        storage.Value.MaxUploadBytes,
        storage.Value.MaxImageUploadBytes,
        transcoder.IsAvailable,
        transcode.Value.BitrateKbps));
}

/// <param name="HistoryThresholdSeconds">Сколько секунд должен проиграться трек, чтобы попасть в историю прослушиваний.</param>
/// <param name="HistoryRetentionEntries">Максимальное число записей истории на пользователя, после чего старые удаляются.</param>
/// <param name="MaxUploadBytes">Максимальный размер файла (в байтах) для загрузки одного аудиотрека.</param>
/// <param name="MaxImageUploadBytes">Максимальный размер файла (в байтах) для загрузки обложки/аватара.</param>
/// <param name="DataSaverAvailable">Доступно ли серверное транскодирование аудио — включает режим экономии трафика на клиенте.</param>
/// <param name="TranscodeBitrateKbps">Битрейт (в kbps), с которым сервер транскодирует аудио в режиме экономии трафика.</param>
public record ClientConfigDto(
    int HistoryThresholdSeconds,
    int HistoryRetentionEntries,
    long MaxUploadBytes,
    long MaxImageUploadBytes,
    bool DataSaverAvailable,
    int TranscodeBitrateKbps);
