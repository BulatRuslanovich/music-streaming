// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Storage;

public class FileSystemImportSource : IImportSource
{
    private const string ArchiveDirectory = ".imported";
    private const string FailedDirectory = ".failed";
    private const int BufferSize = 64 * 1024;

    private readonly string _root;
    private readonly LibraryImportOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<FileSystemImportSource> _logger;

    public FileSystemImportSource(
        IOptions<StorageOptions> storageOptions,
        IOptions<LibraryImportOptions> importOptions,
        TimeProvider clock,
        ILogger<FileSystemImportSource> logger)
    {
        _options = importOptions.Value;
        _clock = clock;
        _logger = logger;

        var storageRoot = Path.GetFullPath(storageOptions.Value.RootPath);
        _root = Path.GetFullPath(Path.Combine(storageRoot, _options.Directory));

        if (!_root.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && _root != storageRoot)
        {
            throw new InvalidOperationException(
                $"LibraryImport:Directory must stay inside Storage:RootPath, got '{_options.Directory}'.");
        }

        if (_options.Enabled)
        {
            Directory.CreateDirectory(_root);
            _logger.LogInformation("Library import folder is {Root}", _root);
        }
    }

    public string DisplayPath => _root;

    public int Count(CancellationToken ct = default) => Enumerate(ct).Count();

    public IReadOnlyList<ImportFile> Take(int limit, TimeSpan minimumAge, CancellationToken ct = default)
    {
        var cutoff = _clock.GetUtcNow() - minimumAge;

        return
        [
            .. Enumerate(ct)
                .Where(file => file.ModifiedAt <= cutoff)
                .OrderBy(file => file.ModifiedAt)
                .Take(Math.Max(1, limit))
        ];
    }

    public Stream OpenRead(ImportFile file) =>
        new FileStream(
            Resolve(file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, useAsync: true);

    public void Consume(ImportFile file)
    {
        var absolutePath = Resolve(file.RelativePath);

        try
        {
            if (_options.AfterImport == ImportDisposition.Move)
                MoveAside(absolutePath, file.RelativePath, ArchiveDirectory);
            else
                File.Delete(absolutePath);

            PruneEmptyParents(absolutePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex, "Imported {File} but could not clear it from the drop folder", file.RelativePath);
        }
    }

    public void Quarantine(ImportFile file, string reason)
    {
        var absolutePath = Resolve(file.RelativePath);

        try
        {
            var moved = MoveAside(absolutePath, file.RelativePath, FailedDirectory);
            File.WriteAllText(moved + ".txt", reason + Environment.NewLine);

            PruneEmptyParents(absolutePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not quarantine {File}", file.RelativePath);
        }
    }

    private IEnumerable<ImportFile> Enumerate(CancellationToken ct)
    {
        if (!_options.Enabled || !Directory.Exists(_root))
            return [];

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
        };

        return Directory.EnumerateFiles(_root, "*", options)
            .TakeWhile(_ => !ct.IsCancellationRequested)
            .Select(absolutePath => new { absolutePath, relative = RelativeOf(absolutePath) })
            .Where(entry => !IsReserved(entry.relative) && AudioUpload.For(entry.relative) is not null)
            .Select(entry => Describe(entry.absolutePath, entry.relative))
            .OfType<ImportFile>();
    }

    private ImportFile? Describe(string absolutePath, string relativePath)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            return info.Exists
                ? new ImportFile(relativePath, info.Name, info.Length, info.LastWriteTimeUtc)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string RelativeOf(string absolutePath) =>
        Path.GetRelativePath(_root, absolutePath).Replace('\\', '/');

    private static bool IsReserved(string relativePath) =>
        relativePath.StartsWith(ArchiveDirectory + "/", StringComparison.Ordinal)
        || relativePath.StartsWith(FailedDirectory + "/", StringComparison.Ordinal)
        || relativePath.Split('/').Any(segment => segment.StartsWith('.'));

    private string Resolve(string relativePath)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(_root, relativePath));

        if (!absolutePath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Import path '{relativePath}' escapes the drop folder.");

        return absolutePath;
    }

    private string MoveAside(string absolutePath, string relativePath, string bucket)
    {
        var target = Path.GetFullPath(Path.Combine(_root, bucket, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        target = Unique(target);
        File.Move(absolutePath, target);

        return target;
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var suffix = 2; ; suffix++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private void PruneEmptyParents(string absolutePath)
    {
        var directory = Path.GetDirectoryName(absolutePath);

        while (directory is not null
               && directory.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
