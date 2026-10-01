// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Recommendations;
using App.Recommendations.Sources;
using App.Services;
using App.Services.Integrations;
using App.Services.Recommendations;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<LoginAttemptTracker>();

        services.AddMemoryCache();
        services.AddSingleton<InlineBuildGate>();
        services.AddSingleton<EventIngestQueue>();
        services.AddSingleton<RecommendationRefreshQueue>();

        services.AddScoped<EventIngestService>();
        services.AddScoped<ProfileBatchLoader>();
        services.AddScoped<AffinityUpdater>();
        services.AddScoped<DerivedTasteRefresher>();
        services.AddScoped<TasteVectorFolder>();
        services.AddScoped<TasteVectorReader>();
        services.AddScoped<TransitionRecorder>();
        services.AddScoped<FlowQueueService>();
        services.AddScoped<ProfileRollupService>();

        services.AddScoped<ICandidateSource, EmbeddingSeedSource>();
        services.AddScoped<ICandidateSource, LovedArtistsSource>();
        services.AddScoped<ICandidateSource, LovedGenresSource>();
        services.AddScoped<ICandidateSource, SharedPlaylistsSource>();
        services.AddScoped<ICandidateSource, EmbeddingTasteSource>();
        services.AddScoped<ICandidateSource, GlobalSource>();
        services.AddScoped<ICandidateSource, UnheardSource>();

        services.AddScoped<CandidateGenerator>();
        services.AddScoped<ShelfGenerationService>();
        services.AddScoped<ShelfHydrator>();
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
        services.AddScoped<SearchService>();
        services.AddScoped<StreamingService>();
        services.AddScoped<CoverStreamService>();

        return services;
    }
}
