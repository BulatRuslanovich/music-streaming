// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

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

/// <summary>
/// Что значит «трек прогрет». Одно определение на всех: и на прогрев при загрузке, и на бэкфилл,
/// иначе они разъедутся и часть библиотеки останется без рендишнов.
/// </summary>
public static class TranscodeWarmup
{
    /// <summary>
    /// High сюда не входит: он тяжёлый, а на узком канале всё равно не выбирается. Его
    /// по-прежнему готовят лениво, по первому запросу.
    /// </summary>
    public static readonly AudioQuality[] Qualities = [AudioQuality.Low, AudioQuality.Normal];

    private static readonly TranscodeKind[] Kinds = [TranscodeKind.Opus, TranscodeKind.Hls];

    /// <summary>Полный набор рендишнов, который должен быть у трека.</summary>
    public static IEnumerable<TranscodeRequest> For(string contentHash, string sourceRelativePath) =>
        from quality in Qualities
        from kind in Kinds
        select new TranscodeRequest(contentHash, sourceRelativePath, quality, kind);

    /// <summary>Те из них, которых ещё нет на диске.</summary>
    public static IReadOnlyList<TranscodeRequest> Missing(
        IEnumerable<(string ContentHash, string SourceRelativePath)> tracks,
        Func<TranscodeRequest, bool> isOnDisk) =>
        [
            .. tracks
                .SelectMany(track => For(track.ContentHash, track.SourceRelativePath))
                .Where(request => !isOnDisk(request)),
        ];
}

public class TranscodeQueue : IWorkQueue<TranscodeRequest>
{
    private const int Capacity = 128;

    private readonly DeduplicatingChannel<TranscodeRequest, string> _queue =
        new(Capacity, BoundedChannelFullMode.DropWrite, request => request.Key, StringComparer.Ordinal);

    public bool TryEnqueue(TranscodeRequest request) => _queue.TryEnqueue(request);

    public IAsyncEnumerable<TranscodeRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.ReadAllAsync(cancellationToken);

    public void MarkFinished(TranscodeRequest request) => _queue.MarkFinished(request);
}
