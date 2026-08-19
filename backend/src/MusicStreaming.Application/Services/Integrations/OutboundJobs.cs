// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Application.Services.Integrations;

public class OutboundJobQueue(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<int> EnqueueAsync(IReadOnlyList<OutboundJob> jobs, CancellationToken ct = default)
    {
        if (jobs.Count == 0)
            return 0;

        var keys = jobs.Select(job => job.DedupeKey).ToList();

        var taken = await db.OutboundJobs.AsNoTracking()
            .Where(job => keys.Contains(job.DedupeKey))
            .Select(job => job.DedupeKey)
            .ToListAsync(ct);

        var known = taken.ToHashSet(StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        var fresh = new List<OutboundJob>(jobs.Count);

        foreach (var job in jobs)
        {
            if (!known.Add(job.DedupeKey))
                continue;

            job.CreatedAt = now;
            job.NextAttemptAt = now;
            fresh.Add(job);
        }

        if (fresh.Count == 0)
            return 0;

        db.OutboundJobs.AddRange(fresh);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var job in fresh)
                db.OutboundJobs.Entry(job).State = EntityState.Detached;

            return 0;
        }

        return fresh.Count;
    }
}
