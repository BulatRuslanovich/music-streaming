// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;

namespace MusicStreaming.Application.Services;

public class LibraryImportState
{
    private readonly Lock _gate = new();

    private bool _running;
    private int _imported;
    private int _failed;
    private int _pending;
    private string? _currentFile;
    private readonly List<UploadFailureDto> _recentFailures = [];

    public const int RecentFailureLimit = 20;

    public bool TryBegin()
    {
        lock (_gate)
        {
            if (_running)
                return false;

            _running = true;
            _imported = 0;
            _failed = 0;
            _pending = 0;
            _currentFile = null;
            _recentFailures.Clear();
            return true;
        }
    }

    public void End()
    {
        lock (_gate)
        {
            _running = false;
            _currentFile = null;
            _pending = 0;
        }
    }

    public void ReportPending(int pending)
    {
        lock (_gate)
            _pending = pending;
    }

    public void ReportStarted(string fileName)
    {
        lock (_gate)
            _currentFile = fileName;
    }

    public void ReportImported()
    {
        lock (_gate)
        {
            _imported++;
            if (_pending > 0)
                _pending--;
        }
    }

    public void ReportFailed(UploadFailureDto failure)
    {
        lock (_gate)
        {
            _failed++;
            if (_pending > 0)
                _pending--;

            _recentFailures.Add(failure);
            if (_recentFailures.Count > RecentFailureLimit)
                _recentFailures.RemoveAt(0);
        }
    }

    public LibraryImportStatusDto Snapshot(bool enabled, string directory, int waiting)
    {
        lock (_gate)
            return new LibraryImportStatusDto(
                enabled,
                directory,
                _running,
                waiting,
                _running ? _pending : 0,
                _imported,
                _failed,
                _currentFile,
                [.. _recentFailures]);
    }
}
