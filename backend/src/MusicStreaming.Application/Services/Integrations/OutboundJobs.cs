using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities.Integrations;

namespace MusicStreaming.Application.Services.Integrations;

/// <summary>
/// Постановка исходящих заданий.
///
/// <para>
/// Ставит пачкой и полагается на уникальный индекс по <see cref="OutboundJob.DedupeKey"/>: то же
/// прослушивание может прийти повторно (второй heartbeat, повторный проход роллапа), и отсеивать
/// такое проверкой «есть ли уже» значило бы гоняться с самим собой. База отвечает на этот вопрос
/// один раз и правильно.
/// </para>
/// </summary>
public class OutboundJobQueue(IApplicationDbContext db, TimeProvider clock)
{
    /// <summary>Добавляет задания, пропуская те, чей ключ уже занят. Возвращает, сколько встало в очередь.</summary>
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
            // Кто-то успел поставить то же задание между проверкой и вставкой. Уникальный индекс
            // сработал ровно как задумано, и терять из-за этого весь пакет незачем.
            foreach (var job in fresh)
                db.OutboundJobs.Entry(job).State = EntityState.Detached;

            return 0;
        }

        return fresh.Count;
    }
}
