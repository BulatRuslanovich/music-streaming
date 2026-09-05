// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Sources;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Admin;
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
        services.AddSingleton<ConnectRegistry>();
        services.AddScoped<ConnectTrackService>();
        services.AddSingleton<LoginAttemptTracker>();
        services.AddSingleton<LibraryImportState>();

        services.AddMemoryCache();
        services.AddSingleton<RecommendationMetrics>();
        services.AddSingleton<StreamingMetrics>();
        services.AddSingleton<EventIngestQueue>();
        services.AddSingleton<ImpressionQueue>();
        services.AddSingleton<RecommendationRefreshQueue>();

        services.AddScoped<EventIngestService>();
        services.AddScoped<ProfileBatchLoader>();
        services.AddScoped<AffinityUpdater>();
        services.AddScoped<DerivedTasteRefresher>();
        services.AddScoped<ProfileRollupService>();
        services.AddScoped<TrackNeighbourLookup>();
        services.AddCandidateSources();
        services.AddScoped<CandidateGenerator>();
        services.AddScoped<ShelfGenerationService>();
        services.AddScoped<ShelfHydrator>();
        services.AddScoped<RecommendationFeedbackService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<DjSessionService>();
        services.AddScoped<RadioService>();

        services.AddScoped<OutboundJobQueue>();
        services.AddScoped<ScrobbleQueueing>();
        services.AddScoped<LastfmService>();
        services.AddScoped<LastfmOAuthState>();
        services.AddScoped<LibraryEnrichment>();

        services.AddScoped<AuthService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<UserSettingsService>();
        services.AddScoped<ClientConfigService>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<MonthlyRecapService>();
        services.AddScoped<NormalizationService>();
        services.AddScoped<AdminStatisticsScope>();
        services.AddScoped<AdminOverviewService>();
        services.AddScoped<AdminListenerBreakdown>();
        services.AddScoped<AdminListenerStatisticsService>();
        services.AddScoped<AdminUploadStatisticsService>();
        services.AddScoped<AdminCatalogHealthService>();
        services.AddScoped<LyricsService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<LibraryOverviewService>();
        services.AddScoped<DailyMixSnapshotStore>();
        services.AddScoped<HomeFeedService>();
        services.AddScoped<TagResolver>();
        services.AddScoped<TrackEditService>();
        services.AddScoped<AlbumEditService>();
        services.AddScoped<LibraryImportService>();
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

        return services;
    }

    /// <summary>
    /// Порядок здесь — это порядок опроса источников в <see cref="CandidateGenerator"/>, а он
    /// значим: числовые сигналы сливаются по максимуму, но источник и текст объяснения достаются
    /// тому, кто назвал трек первым. Менять порядок — менять подписи на полках; проверяется
    /// через <c>make eval</c>.
    /// </summary>
    private static void AddCandidateSources(this IServiceCollection services)
    {
        services.AddScoped<ICandidateSource, ContinueListeningSource>();
        services.AddScoped<ICandidateSource, SimilarToRecentSource>();
        services.AddScoped<ICandidateSource, LovedArtistsSource>();
        services.AddScoped<ICandidateSource, SimilarArtistsSource>();
        services.AddScoped<ICandidateSource, SimilarListenersSource>();
        services.AddScoped<ICandidateSource, LovedGenresSource>();
        services.AddScoped<ICandidateSource, SharedPlaylistsSource>();
        services.AddScoped<ICandidateSource, GlobalSource>();
        services.AddScoped<ICandidateSource, UnheardSource>();

        // Радио вокруг трека добирает из глобального источника напрямую, когда соседей мало.
        services.AddScoped<GlobalSource>();
    }
}
