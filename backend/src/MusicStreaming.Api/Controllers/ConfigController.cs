using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

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
        AvailableQualities()));

    /// <summary>
    /// Ступени, которые эта установка действительно может отдать. Без ffmpeg остаётся только
    /// исходник — и клиент показывает один вариант, а не четыре, три из которых молча
    /// откатываются к тому же файлу.
    /// </summary>
    private IReadOnlyList<AudioQualityDto> AvailableQualities()
    {
        var qualities = new List<AudioQualityDto>();

        if (transcoder.IsAvailable)
        {
            foreach (var quality in (AudioQuality[])[AudioQuality.Low, AudioQuality.Normal, AudioQuality.High])
                qualities.Add(new AudioQualityDto(quality, transcode.Value.BitrateFor(quality)));
        }

        qualities.Add(new AudioQualityDto(AudioQuality.Original, null));
        return qualities;
    }
}

/// <param name="Quality">Ступень качества.</param>
/// <param name="BitrateKbps">Битрейт ступени; <c>null</c> у исходника, битрейт которого свой у каждого файла.</param>
public record AudioQualityDto(AudioQuality Quality, int? BitrateKbps);

/// <param name="HistoryThresholdSeconds">Сколько секунд должен проиграться трек, чтобы попасть в историю прослушиваний.</param>
/// <param name="HistoryRetentionEntries">Максимальное число записей истории на пользователя, после чего старые удаляются.</param>
/// <param name="MaxUploadBytes">Максимальный размер файла (в байтах) для загрузки одного аудиотрека.</param>
/// <param name="MaxImageUploadBytes">Максимальный размер файла (в байтах) для загрузки обложки/аватара.</param>
/// <param name="AudioQualities">Ступени качества, доступные на этой установке, от самой экономной к исходнику.</param>
public record ClientConfigDto(
    int HistoryThresholdSeconds,
    int HistoryRetentionEntries,
    long MaxUploadBytes,
    long MaxImageUploadBytes,
    IReadOnlyList<AudioQualityDto> AudioQualities);
