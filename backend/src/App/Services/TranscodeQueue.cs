// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using App.Common;
using Domain.Common;

namespace App.Services;

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

    public static bool Worthwhile(AudioQuality quality, string? codec, int? sourceKbps) =>
        AudioUpload.IsLossless(codec) || sourceKbps is null || AudioBitrates.For(quality) < sourceKbps;

    public static IEnumerable<TranscodeRequest> For(
        string contentHash, string sourceRelativePath, string? codec, int? sourceKbps) =>
        Qualities
            .Where(quality => Worthwhile(quality, codec, sourceKbps))
            .Select(quality => new TranscodeRequest(contentHash, sourceRelativePath, quality));

    public static IReadOnlyList<TranscodeRequest> Missing(
        IEnumerable<(string ContentHash, string SourceRelativePath, string? Codec, int? BitrateKbps)> tracks,
        Func<TranscodeRequest, bool> isOnDisk) =>
        [
            .. tracks
                .SelectMany(track => For(track.ContentHash, track.SourceRelativePath, track.Codec, track.BitrateKbps))
                .Where(request => !isOnDisk(request)),
        ];
}

public class TranscodeQueue
{
    private readonly Channel<TranscodeRequest> _urgent = Channel.CreateBounded<TranscodeRequest>(
        new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    private readonly Channel<TranscodeRequest> _warmup = Channel.CreateBounded<TranscodeRequest>(
        new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.Wait });

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
