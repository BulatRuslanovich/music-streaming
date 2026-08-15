using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;

namespace MusicStreaming.Infrastructure.Audio;

public class TranscodeWorker(
    TranscodeQueue queue,
    IAudioTranscoder transcoder,
    IMusicStorage storage,
    IOptions<TranscodeOptions> options,
    ILogger<TranscodeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!transcoder.IsAvailable)
            return;

        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transcoding {Key} failed unexpectedly", request.Key);
            }
            finally
            {
                queue.MarkFinished(request);
            }
        }
    }

    private async Task ProcessAsync(TranscodeRequest request, CancellationToken ct)
    {
        if (options.Value.BitrateFor(request.Quality) is not { } bitrate)
            return;

        var targetRelativePath = storage.TranscodePathFor(request.ContentHash, request.Quality);
        if (storage.ResolveExisting(targetRelativePath) is not null)
            return;

        var source = storage.ResolveExisting(request.SourceRelativePath);
        if (source is null)
        {
            logger.LogWarning(
                "Skipped transcoding {Key}: {Path} is missing from storage",
                request.Key, request.SourceRelativePath);
            return;
        }

        var target = storage.ResolveForWrite(targetRelativePath);
        var startedAt = Stopwatch.GetTimestamp();

        if (!await transcoder.TranscodeToOpusAsync(source, target, bitrate, ct))
            return;

        logger.LogInformation(
            "Cached the {Quality} rendition of {Hash} in {Elapsed:0.0} s: {SourceBytes} → {TargetBytes} bytes",
            request.Quality,
            request.ContentHash,
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            new FileInfo(source).Length,
            new FileInfo(target).Length);
    }
}
