// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;
using MusicStreaming.Infrastructure.Storage;
using Xunit;

namespace MusicStreaming.UnitTests;

public sealed class HlsStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"caimack-hls-storage-{Guid.CreateVersion7():N}");

    [Fact]
    public void A_variant_is_ready_only_when_its_playlist_init_and_segment_exist()
    {
        var storage = Storage();
        var directory = storage.VariantDirectory("abc123", AudioQuality.Normal);
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(directory, "index.m3u8"), "#EXTM3U");
        File.WriteAllBytes(Path.Combine(directory, "init.mp4"), [1]);
        Assert.False(storage.HlsVariantReady("abc123", AudioQuality.Normal));

        File.WriteAllBytes(Path.Combine(directory, "segment-00000.m4s"), [2, 3]);
        Assert.True(storage.HlsVariantReady("abc123", AudioQuality.Normal));

        using var segment = storage.OpenHlsFile("abc123", AudioQuality.Normal, "segment-00000.m4s");
        Assert.NotNull(segment);
        Assert.Equal(2, segment.Length);
    }

    [Fact]
    public void Hls_assets_cannot_escape_their_variant_directory()
    {
        var storage = Storage();

        Assert.Throws<UnauthorizedAccessException>(() =>
            storage.OpenHlsFile("abc123", AudioQuality.Low, "../normal/index.m3u8"));
    }

    [Fact]
    public void Deleting_transcodes_removes_the_hls_tree()
    {
        var storage = Storage();
        var directory = storage.VariantDirectory("abc123", AudioQuality.Low);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "index.m3u8"), "#EXTM3U");

        storage.DeleteTranscodes("abc123");

        Assert.False(Directory.Exists(Path.Combine(_root, "hls", "abc123")));
    }

    private FileSystemHlsStorage Storage() => new(new StorageRoot(
        Options.Create(new StorageOptions { RootPath = _root }),
        NullLogger<StorageRoot>.Instance));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
