// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using App.Common;
using App.Dtos;

namespace App.Services.Integrations;

// Импорты живут только в памяти: после перезапуска сервера список пуст, а уже добавленные треки
// остаются в библиотеке. Повторный импорт тех же лайков просто пропустит их.
public class YandexImportQueue(TimeProvider clock)
{
    private const int KeptJobs = 20;

    private readonly Channel<YandexImportJob> _channel =
        Channel.CreateUnbounded<YandexImportJob>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<Guid, YandexImportJob> _jobs = new();

    public YandexImportJob Enqueue(YandexImportKind kind, string? sourceId, string token)
    {
        if (_jobs.Values.Any(job => job.IsActive && job.Kind == kind && job.SourceId == sourceId))
            throw new ConflictException("This import is already running.");

        // Язык запроса запоминается: причины отказов воркер пишет на нём, а не на языке сервера.
        var job = new YandexImportJob(kind, sourceId, token, CultureInfo.CurrentUICulture, clock.GetUtcNow());
        _jobs[job.Id] = job;
        ForgetOldest();

        _channel.Writer.TryWrite(job);
        return job;
    }

    public IReadOnlyList<YandexImportDto> List() =>
        [.. _jobs.Values.OrderByDescending(job => job.CreatedAt).Select(job => job.ToDto())];

    // Идущий импорт останавливается после текущего трека, закончившийся убирается из списка.
    public bool Remove(Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job))
            return false;

        if (job.IsActive)
            job.Cancel();
        else
            _jobs.TryRemove(id, out _);

        return true;
    }

    public IAsyncEnumerable<YandexImportJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    private void ForgetOldest()
    {
        foreach (var job in _jobs.Values
                     .Where(job => !job.IsActive)
                     .OrderByDescending(job => job.CreatedAt)
                     .Skip(KeptJobs))
        {
            _jobs.TryRemove(job.Id, out _);
        }
    }
}

public sealed class YandexImportJob(
    YandexImportKind kind,
    string? sourceId,
    string token,
    CultureInfo language,
    DateTimeOffset createdAt)
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<YandexImportFailureDto> _failures = [];

    private YandexImportStatus _status = YandexImportStatus.Queued;
    private string? _title;
    private string? _error;
    private string? _current;
    private int _total;
    private int _done;
    private int _added;
    private int _skipped;

    public Guid Id { get; } = Guid.CreateVersion7();
    public YandexImportKind Kind { get; } = kind;
    public string? SourceId { get; } = sourceId;
    public string Token { get; } = token;
    public CultureInfo Language { get; } = language;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public CancellationToken Cancelled => _cancellation.Token;

    public bool IsActive
    {
        get
        {
            lock (_lock)
                return _status is YandexImportStatus.Queued or YandexImportStatus.Running;
        }
    }

    public void Cancel() => _cancellation.Cancel();

    public void Start(string? title, int total)
    {
        lock (_lock)
        {
            _status = YandexImportStatus.Running;
            _title = title;
            _total = total;
        }
    }

    public void Importing(string trackTitle)
    {
        lock (_lock)
            _current = trackTitle;
    }

    public void Added() => Count(ref _added);

    public void Skipped() => Count(ref _skipped);

    public void Failed(string trackTitle, string reason)
    {
        lock (_lock)
        {
            _failures.Add(new YandexImportFailureDto(trackTitle, reason));
            _done++;
            _current = null;
        }
    }

    public void Finish()
    {
        lock (_lock)
        {
            _status = _cancellation.IsCancellationRequested ? YandexImportStatus.Cancelled : YandexImportStatus.Finished;
            _current = null;
        }
    }

    public void Fail(string error)
    {
        lock (_lock)
        {
            _status = YandexImportStatus.Failed;
            _error = error;
            _current = null;
        }
    }

    public YandexImportDto ToDto()
    {
        lock (_lock)
        {
            return new YandexImportDto(
                Id, Kind, _title, _status, _error, _total, _done, _added, _skipped, _current, [.. _failures], CreatedAt);
        }
    }

    private void Count(ref int counter)
    {
        lock (_lock)
        {
            counter++;
            _done++;
            _current = null;
        }
    }
}
