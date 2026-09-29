// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Sources;
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
        services.AddCandidateSources();
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
        services.AddScoped<StatisticsService>();
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

    /// <summary>
    /// Порядок здесь — это порядок опроса источников в <see cref="CandidateGenerator"/>, а он
    /// значим: числовые сигналы сливаются по максимуму, но источник и текст объяснения достаются
    /// тому, кто назвал трек первым. Менять порядок — менять подписи на полках; проверяется
    /// через <c>make eval</c>.
    /// </summary>
    private static void AddCandidateSources(this IServiceCollection services)
    {
        // Первым: «звучит как то, что вы только что играли» называет трек, который слушатель
        // помнит, и объясняет лучше, чем «вам нравится этот артист».
        services.AddScoped<ICandidateSource, EmbeddingSeedSource>();

        services.AddScoped<ICandidateSource, LovedArtistsSource>();
        services.AddScoped<ICandidateSource, LovedGenresSource>();
        services.AddScoped<ICandidateSource, SharedPlaylistsSource>();

        // Предпоследним: этот источник назовёт огромное число треков, а сказать о них может
        // только «подходит вашему вкусу». Поздняя регистрация оставляет ему подпись лишь там,
        // где больше никто трек не нашёл, — в чём и есть его ценность.
        services.AddScoped<ICandidateSource, EmbeddingTasteSource>();

        services.AddScoped<ICandidateSource, GlobalSource>();
        services.AddScoped<ICandidateSource, UnheardSource>();

    }
}
