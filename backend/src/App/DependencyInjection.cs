// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Services;
using App.Services.Integrations;
using App.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Home;
using App.Recommendations.Moods;
using App.Recommendations.Radio;

namespace App;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TranscodeQueue>();
        services.AddSingleton<AudioEmbeddingQueue>();
        services.AddSingleton<LibraryEnrichmentQueue>();
        services.AddSingleton<PlaybackSessionRegistry>();

        services.AddMemoryCache();
        services.AddSingleton<InlineBuildGate>();
        services.AddSingleton<RecommendationRefreshQueue>();
        services.AddSingleton<EmbeddingIndex>();
        services.AddSingleton<MoodCatalog>();

        services.AddScoped<EventIngestService>();
        services.AddScoped<ProfileRollupService>();


        services.AddScoped<CandidatePool>();
        services.AddScoped<ShelfGenerationService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<RadioService>();

        services.AddScoped<LibraryEnrichment>();

        services.AddScoped<AuthService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<UserSettingsService>();
        services.AddScoped<ClientConfigService>();
        services.AddScoped<LyricsService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<LibraryOverviewService>();
        services.AddScoped<DailyMixSnapshotStore>();
        services.AddScoped<HomeFeedService>();
        services.AddScoped<TagResolver>();
        services.AddScoped<TrackEditService>();
        services.AddScoped<AlbumEditService>();
        services.AddScoped<ArtistProfileService>();
        services.AddScoped<TrackAssembler>();
        services.AddScoped<TrackPostProcessing>();
        services.AddScoped<TrackUploadService>();
        services.AddScoped<UploadProbeService>();
        services.AddScoped<PlaylistService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<RecapService>();
        services.AddScoped<PlaybackHandoffService>();
        services.AddScoped<SearchService>();
        services.AddScoped<StreamingService>();
        services.AddScoped<CoverStreamService>();

        return services;
    }
}
