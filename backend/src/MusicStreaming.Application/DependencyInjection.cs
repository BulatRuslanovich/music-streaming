// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TranscodeQueue>();
        services.AddSingleton<AudioAnalysisQueue>();
        services.AddSingleton<LibraryEnrichmentQueue>();
        services.AddSingleton<PlaybackSessionRegistry>();

        services.AddMemoryCache();
        services.AddSingleton<RecommendationMetrics>();
        services.AddSingleton<StreamingMetrics>();
        services.AddSingleton<EventIngestQueue>();
        services.AddSingleton<RecommendationRefreshQueue>();

        services.AddScoped<EventIngestService>();
        services.AddScoped<ProfileRollupService>();
        services.AddScoped<CandidateGenerator>();
        services.AddScoped<ShelfGenerationService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<RecommendationDiagnosticsService>();

        services.AddScoped<DjSessionService>();
        services.AddScoped<RadioService>();

        services.AddScoped<OutboundJobQueue>();
        services.AddScoped<ScrobbleQueueing>();
        services.AddScoped<LastfmService>();
        services.AddScoped<LibraryEnrichment>();

        services.AddScoped<AuthService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<UserSettingsService>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<LyricsService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<HomeFeedService>();
        services.AddScoped<TagResolver>();
        services.AddScoped<TrackEditService>();
        services.AddScoped<ArtistProfileService>();
        services.AddScoped<TrackUploadService>();
        services.AddScoped<UploadProbeService>();
        services.AddScoped<PlaylistService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<SearchService>();
        services.AddScoped<StreamingService>();

        return services;
    }
}
