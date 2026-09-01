// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Integrations;

/// <summary>
/// Очередь обогащения наполняется только при загрузке, поэтому у библиотеки, собранной до появления
/// тегов, их не будет никогда. Этот проход добирает их порциями: сначала артисты — их теги
/// достаются всем трекам, — потом сами треки.
/// </summary>
public class TagBackfillWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TagEnrichmentOptions> options,
    ILogger<TagBackfillWorker> logger) : ScheduledWorker(scopeFactory, logger)
{
    private TagEnrichmentOptions Options => options.Value;

    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(2);
    protected override TimeSpan? Interval => TimeSpan.FromHours(1);
    protected override string Name => "Tag backfill";

    protected override bool ShouldRun()
    {
        if (!Options.Enabled || Options.BackfillBatchSize == 0)
            return false;

        using var scope = CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IMusicTagProvider>().IsConfigured)
            return true;

        logger.LogInformation("Tag backfill is idle: no tag provider is configured");
        return false;
    }

    protected override async Task RunPassAsync(CancellationToken ct)
    {
        var artists = await PendingAsync(
            db => db.Artists.Where(a => a.TagsFetchedAt == null).OrderBy(a => a.CreatedAt).Select(a => a.Id),
            ct);

        var enriched = 0;

        foreach (var artistId in artists)
        {
            enriched += await RunAsync(
                (enrichment, token) => enrichment.EnrichArtistTagsAsync(artistId, token), ct);

            await Task.Delay(Options.RequestDelayMs, ct);
        }

        var tracks = await PendingAsync(
            db => db.Tracks.Where(t => t.TagsFetchedAt == null).OrderBy(t => t.CreatedAt).Select(t => t.Id),
            ct);

        foreach (var trackId in tracks)
        {
            enriched += await RunAsync(
                (enrichment, token) => enrichment.EnrichTrackTagsAsync(trackId, token), ct);

            await Task.Delay(Options.RequestDelayMs, ct);
        }

        if (artists.Count + tracks.Count > 0)
        {
            logger.LogInformation(
                "Tag backfill pass looked up {Artists} artists and {Tracks} tracks, {Enriched} came back tagged",
                artists.Count, tracks.Count, enriched);
        }
    }

    private async Task<List<Guid>> PendingAsync(
        Func<ApplicationDbContext, IQueryable<Guid>> pending, CancellationToken ct)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await pending(db).Take(Options.BackfillBatchSize).ToListAsync(ct);
    }

    private async Task<int> RunAsync(
        Func<LibraryEnrichment, CancellationToken, Task<EnrichmentResult>> step, CancellationToken ct)
    {
        try
        {
            using var scope = CreateScope();
            var enrichment = scope.ServiceProvider.GetRequiredService<LibraryEnrichment>();

            return await step(enrichment, ct) is { Status: EnrichmentStatus.Saved } ? 1 : 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Tag backfill step failed");
            return 0;
        }
    }
}
