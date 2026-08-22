// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

internal static class RecommendationRunPersistence
{
    public static async Task TrySaveAsync(
        IServiceScopeFactory scopeFactory,
        RecommendationRun run,
        ILogger logger,
        string warningMessage)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.RecommendationRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, warningMessage, run.Id);
        }
    }
}
