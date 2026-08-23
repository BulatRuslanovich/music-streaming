// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Entities.Recommendations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Recommendations;

internal static class RecommendationRunPersistence
{
    /// <summary>Столько места под текст ошибки отведено колонке <c>recommendation_runs.error</c>.</summary>
    private const int MaxErrorLength = 2000;

    public static void MarkFailed(RecommendationRun run, Exception failure)
    {
        run.Status = RecommendationRunStatus.Failed;
        run.Error = Text.Truncate(failure.Message, MaxErrorLength);
    }

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
