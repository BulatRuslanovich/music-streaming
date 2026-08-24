// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Domain.Entities.Integrations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Integrations;

public class OutboundJobWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<OutboundJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(250);
    private const int BatchSize = 50;

    private const int MaxErrorLength = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbound job processing failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lastfm = scope.ServiceProvider.GetRequiredService<ILastfmApi>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        if (!lastfm.IsConfigured)
            return;

        var now = clock.GetUtcNow();

        var due = await db.OutboundJobs
            .Where(job => job.State == OutboundJobState.Pending && job.NextAttemptAt <= now)
            .OrderBy(job => job.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        var sessions = await SessionsAsync(db, secrets, due, ct);

        foreach (var job in due)
        {
            if (!sessions.TryGetValue(job.UserId, out var session))
            {
                job.State = OutboundJobState.Failed;
                job.LastError = "The Last.fm account is no longer connected.";
            }
            else
            {
                await RunAsync(db, lastfm, job, session, ct);
            }

            await db.SaveChangesAsync(CancellationToken.None);

            if (ct.IsCancellationRequested)
                break;

            await Task.Delay(Pace, ct);
        }
    }

    private async Task RunAsync(
        ApplicationDbContext db, ILastfmApi lastfm, OutboundJob job, string sessionKey, CancellationToken ct)
    {
        job.Attempts++;

        try
        {
            if (ScrobbleQueueing.ReadPayload(job.Payload) is not { } payload)
                throw new LastfmException("The job payload could not be read.");

            await lastfm.SendAsync(
                new LastfmTrack(
                    payload.Artist,
                    payload.Title,
                    payload.Album,
                    payload.DurationSeconds,
                    payload.PlayedAtUnix is { } unix ? DateTimeOffset.FromUnixTimeSeconds(unix) : null),
                sessionKey,
                ct);

            job.State = OutboundJobState.Succeeded;
            job.LastError = null;

            if (job.Kind == OutboundJobKind.LastfmScrobble)
            {
                await db.LastfmAccounts
                    .Where(a => a.UserId == job.UserId)
                    .ExecuteUpdateAsync(
                        a => a.SetProperty(account => account.LastScrobbleAt, clock.GetUtcNow()), ct);
            }
        }
        catch (LastfmException ex)
        {
            Reschedule(job, ex);
        }
    }

    private void Reschedule(OutboundJob job, LastfmException failure)
    {
        job.LastError = Text.Truncate(failure.Message, MaxErrorLength);

        if (OutboundRetry.DelayFor(job.Kind, job.Attempts, failure) is not { } delay)
        {
            job.State = OutboundJobState.Failed;

            logger.Log(
                failure.IsAuthFailure ? LogLevel.Warning : LogLevel.Information,
                "Outbound job {JobId} ({Kind}) gave up after {Attempts} attempts: {Error}",
                job.Id, job.Kind, job.Attempts, job.LastError);

            return;
        }

        job.NextAttemptAt = clock.GetUtcNow() + delay;

        logger.LogDebug(
            "Outbound job {JobId} retries at {NextAttemptAt}: {Error}",
            job.Id, job.NextAttemptAt, job.LastError);
    }

    private static async Task<Dictionary<Guid, string>> SessionsAsync(
        ApplicationDbContext db,
        ISecretProtector secrets,
        List<OutboundJob> due,
        CancellationToken ct)
    {
        var userIds = due.Select(job => job.UserId).Distinct().ToList();

        var accounts = await db.LastfmAccounts.AsNoTracking()
            .Where(a => a.Enabled && userIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.SessionKey })
            .ToListAsync(ct);

        var sessions = new Dictionary<Guid, string>(accounts.Count);

        foreach (var account in accounts)
        {
            if (secrets.Unprotect(account.SessionKey) is { Length: > 0 } key)
                sessions[account.UserId] = key;
        }

        return sessions;
    }
}
