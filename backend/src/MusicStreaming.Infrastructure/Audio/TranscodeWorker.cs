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
                logger.LogError(ex, "Transcoding {Hash} failed unexpectedly", request.ContentHash);
            }
            finally
            {
                queue.MarkFinished(request.ContentHash);
            }
        }
    }

    private async Task ProcessAsync(TranscodeRequest request, CancellationToken ct)
    {
        var targetRelativePath = storage.TranscodePathFor(request.ContentHash);
        if (storage.ResolveExisting(targetRelativePath) is not null)
            return;

        var source = storage.ResolveExisting(request.SourceRelativePath);
        if (source is null)
        {
            logger.LogWarning(
                "Skipped transcoding {Hash}: {Path} is missing from storage",
                request.ContentHash, request.SourceRelativePath);
            return;
        }

        var target = storage.ResolveForWrite(targetRelativePath);
        var startedAt = Stopwatch.GetTimestamp();

        if (!await transcoder.TranscodeToOpusAsync(source, target, options.Value.BitrateKbps, ct))
            return;

        logger.LogInformation(
            "Cached the Opus rendition of {Hash} in {Elapsed:0.0} s: {SourceBytes} → {TargetBytes} bytes",
            request.ContentHash,
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            new FileInfo(source).Length,
            new FileInfo(target).Length);
    }
}
