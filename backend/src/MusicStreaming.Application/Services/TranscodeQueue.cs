// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Threading.Channels;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public enum TranscodeKind
{
    Opus,
    Hls,
}

public record TranscodeRequest(
    string ContentHash,
    string SourceRelativePath,
    AudioQuality Quality,
    TranscodeKind Kind = TranscodeKind.Opus)
{
    public string Key => $"{ContentHash}:{Quality}:{Kind}";
}

public static class TranscodeWarmup
{
    public static readonly AudioQuality[] Qualities = [AudioQuality.Low, AudioQuality.Normal];

    private static readonly TranscodeKind[] Kinds = [TranscodeKind.Opus, TranscodeKind.Hls];

    public static IEnumerable<TranscodeRequest> For(string contentHash, string sourceRelativePath) =>
        from quality in Qualities
        from kind in Kinds
        select new TranscodeRequest(contentHash, sourceRelativePath, quality, kind);

    public static IReadOnlyList<TranscodeRequest> Missing(
        IEnumerable<(string ContentHash, string SourceRelativePath)> tracks,
        Func<TranscodeRequest, bool> isOnDisk) =>
        [
            .. tracks
                .SelectMany(track => For(track.ContentHash, track.SourceRelativePath))
                .Where(request => !isOnDisk(request)),
        ];
}

// Две полосы с общим набором ключей. On-demand — это трек, который слушают прямо сейчас; прогрев —
// заливка и бэкфилл, работы там на порядки больше. Читают их разные воркеры (см. TranscodeWorker),
// поэтому тысяча фоновых рендишенов не может задержать тот единственный, которого ждёт плеер.
public class TranscodeQueue : IWorkQueue<TranscodeRequest>
{
    private const int Capacity = 128;
    private const int WarmupCapacity = 512;

    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    private readonly DeduplicatingChannel<TranscodeRequest, string> _onDemand;
    private readonly DeduplicatingChannel<TranscodeRequest, string> _warmup;

    public TranscodeQueue()
    {
        _onDemand = new DeduplicatingChannel<TranscodeRequest, string>(
            Capacity, BoundedChannelFullMode.DropWrite, request => request.Key,
            StringComparer.Ordinal, singleReader: true, pending: _pending);

        _warmup = new DeduplicatingChannel<TranscodeRequest, string>(
            WarmupCapacity, BoundedChannelFullMode.DropWrite, request => request.Key,
            StringComparer.Ordinal, singleReader: false, pending: _pending);

        Warmup = new Lane(_warmup);
    }

    /// <summary>Фоновая полоса: заливка и бэкфилл. Читается всеми воркерами, кроме одного.</summary>
    public IWorkQueue<TranscodeRequest> Warmup { get; }

    public bool TryEnqueue(TranscodeRequest request) => _onDemand.TryEnqueue(request);

    public bool TryEnqueueWarmup(TranscodeRequest request) => _warmup.TryEnqueue(request);

    public IAsyncEnumerable<TranscodeRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _onDemand.ReadAllAsync(cancellationToken);

    public void MarkFinished(TranscodeRequest request) => _onDemand.MarkFinished(request);

    private sealed class Lane(DeduplicatingChannel<TranscodeRequest, string> channel)
        : IWorkQueue<TranscodeRequest>
    {
        public IAsyncEnumerable<TranscodeRequest> ReadAllAsync(CancellationToken cancellationToken) =>
            channel.ReadAllAsync(cancellationToken);

        public void MarkFinished(TranscodeRequest request) => channel.MarkFinished(request);
    }
}
