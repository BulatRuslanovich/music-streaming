// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Storage;
using Xunit;

namespace MusicStreaming.UnitTests;

public sealed class FileSystemImportSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"caimack-import-{Guid.CreateVersion7():N}");

    private readonly TestClock _clock = new();

    [Fact]
    public void Only_supported_audio_files_are_picked_up_and_nested_folders_are_walked()
    {
        Drop("Album/01 - Track.mp3");
        Drop("Album/02 - Track.flac");
        Drop("Album/cover.jpg");
        Drop("Album/notes.txt");
        Drop("Single.m4a");

        var source = Source();
        Age();

        var found = Ready(source).Select(file => file.RelativePath).ToList();

        Assert.Equal(3, found.Count);
        Assert.Contains("Album/01 - Track.mp3", found);
        Assert.Contains("Album/02 - Track.flac", found);
        Assert.Contains("Single.m4a", found);
    }

    [Fact]
    public void A_file_that_may_still_be_copying_waits_for_the_next_scan()
    {
        Drop("Fresh.mp3");
        var source = Source();

        Assert.Empty(Ready(source));

        Age();
        Assert.Single(Ready(source));
    }

    [Fact]
    public void Imported_files_are_deleted_and_the_folders_they_left_behind_go_too()
    {
        Drop("Album/01 - Track.mp3");
        var source = Source();
        Age();

        source.Consume(Ready(source, 1)[0]);

        Assert.False(File.Exists(Path.Combine(_root, "Album", "01 - Track.mp3")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Album")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void With_the_move_disposition_an_imported_file_is_archived_instead_of_deleted()
    {
        Drop("Album/01 - Track.mp3");
        var source = Source(ImportDisposition.Move);
        Age();

        source.Consume(Ready(source, 1)[0]);

        Assert.True(File.Exists(Path.Combine(_root, ".imported", "Album", "01 - Track.mp3")));
        Assert.Empty(Ready(source));
    }

    [Fact]
    public void A_rejected_file_moves_aside_with_its_reason_so_it_stops_blocking_later_scans()
    {
        Drop("Broken.mp3");
        var source = Source();
        Age();

        source.Quarantine(Ready(source, 1)[0], "The file contains no audio stream.");

        var quarantined = Path.Combine(_root, ".failed", "Broken.mp3");
        Assert.True(File.Exists(quarantined));
        Assert.Contains("no audio stream", File.ReadAllText(quarantined + ".txt"));
        Assert.Empty(Ready(source));
    }

    [Fact]
    public void Dropping_the_same_name_twice_archives_both_copies_instead_of_overwriting()
    {
        var source = Source(ImportDisposition.Move);

        Drop("Track.mp3");
        Age();
        source.Consume(Ready(source)[0]);

        Drop("Track.mp3");
        Age();
        source.Consume(Ready(source)[0]);

        var archived = Directory.GetFiles(Path.Combine(_root, ".imported"), "*.mp3");
        Assert.Equal(2, archived.Length);
    }

    [Fact]
    public void A_directory_pointing_outside_the_storage_root_is_refused_at_startup()
    {
        var storage = Options.Create(new StorageOptions { RootPath = _root });
        var import = Options.Create(new LibraryImportOptions { Directory = "../escape" });

        Assert.Throws<InvalidOperationException>(() =>
            new FileSystemImportSource(storage, import, _clock, NullLogger<FileSystemImportSource>.Instance));
    }

    [Fact]
    public void A_disabled_import_reports_nothing_and_creates_no_folder()
    {
        var source = Source(enabled: false);

        Assert.Equal(0, source.Count(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(_root, "import")));
    }

    private IImportSource Source(
        ImportDisposition disposition = ImportDisposition.Delete, bool enabled = true) =>
        new FileSystemImportSource(
            Options.Create(new StorageOptions { RootPath = _root }),
            Options.Create(new LibraryImportOptions
            {
                Enabled = enabled,
                Directory = ".",
                AfterImport = disposition,
            }),
            _clock,
            NullLogger<FileSystemImportSource>.Instance);

    private static IReadOnlyList<ImportFile> Ready(IImportSource source, int limit = 50) =>
        source.Take(limit, TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

    private void Drop(string relativePath)
    {
        var absolutePath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllBytes(absolutePath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(absolutePath, _clock.GetUtcNow().UtcDateTime);
    }

    private void Age() => _clock.Advance(TimeSpan.FromMinutes(1));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
