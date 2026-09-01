// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Storage;

/// <summary>Кэш перекодировок и раскладка HLS: то, что ffmpeg производит из оригинала.</summary>
public class FileSystemHlsStorage(StorageRoot root) : IHlsStorage
{
    private readonly ConcurrentDictionary<string, byte> _readyVariants = new(StringComparer.Ordinal);

    public string TranscodePathFor(string contentHash, AudioQuality quality) =>
        $"{StorageRoot.TranscodeDirectory}/{contentHash}.{quality.ToString().ToLowerInvariant()}.opus";

    // Чтение и запись разведены намеренно: раньше здесь стоял CreateDirectory, а зовут этот метод
    // HlsVariantReady и OpenHlsFile — то есть системный вызов на запись случался на каждом GET
    // сегмента, и он же насоздавал пустых директорий для треков, которые никогда не транскодировались.
    // Public для тестов раскладки, но не на IHlsStorage: это деталь файловой реализации, и
    // приложению знать её незачем — оно спрашивает готовность и открывает файл по имени.
    public string VariantDirectory(string contentHash, AudioQuality quality) =>
        root.Resolve($"{StorageRoot.HlsDirectory}/{contentHash}/{quality.ToString().ToLowerInvariant()}");

    public string EnsureHlsVariantDirectory(string contentHash, AudioQuality quality)
    {
        var absolutePath = VariantDirectory(contentHash, quality);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        return absolutePath;
    }

    // Готовность монотонна: рендишен, однажды дописанный на диск, сам собой не исчезает. Поэтому
    // положительный ответ кэшируется навсегда — OpenHlsMasterAsync спрашивает до восьми раз за запрос.
    public bool HlsVariantReady(string contentHash, AudioQuality quality)
    {
        var key = $"{contentHash}:{quality}";
        if (_readyVariants.ContainsKey(key))
            return true;

        var directory = VariantDirectory(contentHash, quality);
        var ready = File.Exists(Path.Combine(directory, "index.m3u8"))
                    && File.Exists(Path.Combine(directory, "init.mp4"))
                    && Directory.EnumerateFiles(directory, "segment-*.m4s").Any();

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
        {
            root.Delete(TranscodePathFor(contentHash, quality));
            _readyVariants.TryRemove($"{contentHash}:{quality}", out _);
        }

        root.TryDeleteDirectory(root.Resolve($"{StorageRoot.HlsDirectory}/{contentHash}"));
    }
}
