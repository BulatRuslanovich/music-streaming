// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Services;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Storage;

public class FileSystemHlsStorage(StorageRoot root) : IHlsStorage
{
    private readonly ConcurrentDictionary<string, byte> _readyVariants = new(StringComparer.Ordinal);

    public string VariantDirectory(string contentHash, AudioQuality quality) =>
        root.Resolve($"{StorageRoot.HlsDirectory}/{contentHash}/{quality.ToString().ToLowerInvariant()}");

    public string EnsureHlsVariantDirectory(string contentHash, AudioQuality quality)
    {
        var absolutePath = VariantDirectory(contentHash, quality);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        return absolutePath;
    }

    public bool HlsVariantReady(string contentHash, AudioQuality quality)
    {
        var key = $"{contentHash}:{quality}";
        if (_readyVariants.ContainsKey(key))
            return true;

        var directory = VariantDirectory(contentHash, quality);
        var ready = File.Exists(Path.Combine(directory, HlsPlaylist.IndexFileName))
                    && File.Exists(Path.Combine(directory, HlsPlaylist.MediaFileName));

        if (ready)
            _readyVariants.TryAdd(key, 0);

        return ready;
    }

    public Stream? OpenHlsFile(string contentHash, AudioQuality quality, string fileName)
    {
        var directory = VariantDirectory(contentHash, quality);
        var absolutePath = Path.GetFullPath(Path.Combine(directory, fileName));
        var directoryWithSeparator = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(directoryWithSeparator, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Rejected HLS asset path '{fileName}'.");

        return StorageRoot.OpenAbsolute(absolutePath);
    }

    public void DeleteTranscodes(string contentHash)
    {
        foreach (var quality in Enum.GetValues<AudioQuality>())
            _readyVariants.TryRemove($"{contentHash}:{quality}", out _);

        root.TryDeleteDirectory(root.Resolve($"{StorageRoot.HlsDirectory}/{contentHash}"));
    }
}
