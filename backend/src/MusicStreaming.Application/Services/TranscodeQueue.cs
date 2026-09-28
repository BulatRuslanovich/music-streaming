// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

/// <summary>One HLS rendition of one original at one bitrate.</summary>
public record TranscodeRequest(
    string ContentHash,
    string SourceRelativePath,
    AudioQuality Quality)
{
    public string Key => $"{ContentHash}:{Quality}";
}

public static class TranscodeWarmup
{
    public static readonly AudioQuality[] Qualities = [AudioQuality.Low, AudioQuality.Normal];

    public static IEnumerable<TranscodeRequest> For(string contentHash, string sourceRelativePath) =>
        Qualities.Select(quality => new TranscodeRequest(contentHash, sourceRelativePath, quality));

    public static IReadOnlyList<TranscodeRequest> Missing(
        IEnumerable<(string ContentHash, string SourceRelativePath)> tracks,
        Func<TranscodeRequest, bool> isOnDisk) =>
        [
            .. tracks
                .SelectMany(track => For(track.ContentHash, track.SourceRelativePath))
                .Where(request => !isOnDisk(request)),
        ];
}

/// <summary>
/// Jobs for ffmpeg in two lanes: urgent (a track a player is waiting for) and warmup
/// (renditions prepared ahead of time after an upload or by the backfill).
/// </summary>
/// <remarks>
/// Полосы читают разные воркеры (см. TranscodeWorker), поэтому сотни фоновых заданий не
/// задерживают то одно, которого ждёт плеер. Код двух полос повторяется намеренно: так каждую
/// видно целиком, без обёрток.
/// <para>
/// Режим Wait, а не DropWrite: переполненный DropWrite-канал молча выбрасывает заявку, но
/// <c>TryWrite</c> всё равно отвечает true — вызывающий не узнаёт о потере, а ключ заявки навсегда
/// остаётся «в очереди». Wait при переполнении честно отвечает false.
/// </para>
/// </remarks>
public class TranscodeQueue
{
    private readonly Channel<TranscodeRequest> _urgent = Channel.CreateBounded<TranscodeRequest>(
        new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly Channel<TranscodeRequest> _warmup = Channel.CreateBounded<TranscodeRequest>(
        new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait });

    // Что уже стоит в полосе — чтобы одна вариация не занимала в ней два места. Учёт у каждой
    // полосы свой: срочная заявка обязана пройти, даже если та же вариация ждёт в фоновой.
    private readonly ConcurrentDictionary<string, byte> _urgentKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _warmupKeys = new(StringComparer.Ordinal);

    public bool TryEnqueueUrgent(TranscodeRequest request)
    {
        if (!_urgentKeys.TryAdd(request.Key, 0))
            return false;

        if (_urgent.Writer.TryWrite(request))
            return true;

        _urgentKeys.TryRemove(request.Key, out _);
        return false;
    }

    public bool TryEnqueueWarmup(TranscodeRequest request)
    {
        if (!_warmupKeys.TryAdd(request.Key, 0))
            return false;

        if (_warmup.Writer.TryWrite(request))
            return true;

        _warmupKeys.TryRemove(request.Key, out _);
        return false;
    }

    // Ключ снимается, как только заявку забрали: пока воркер над ней работает, ту же вариацию
    // можно поставить снова. Повтор дешёвый — воркер увидит готовую вариацию и пропустит его.
    public async IAsyncEnumerable<TranscodeRequest> ReadUrgentAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var request in _urgent.Reader.ReadAllAsync(ct))
        {
            _urgentKeys.TryRemove(request.Key, out _);
            yield return request;
        }
    }

    public async IAsyncEnumerable<TranscodeRequest> ReadWarmupAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var request in _warmup.Reader.ReadAllAsync(ct))
        {
            _warmupKeys.TryRemove(request.Key, out _);
            yield return request;
        }
    }
}
