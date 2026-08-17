using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Services;
using MusicStreaming.Application.Services.Integrations;
using MusicStreaming.Application.Services.Recommendations;

namespace MusicStreaming.Application;

/// <summary>
/// Регистрация сценариев приложения.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Подключает сервисы, очереди и общие вспомогательные объекты слоя приложения.
    ///
    /// <para>
    /// Сервисы регистрируются перечислением, а не автоматическим поиском по сборке: список видно
    /// целиком, и случайно попавший в сборку класс не оказывается доступным для внедрения.
    /// </para>
    ///
    /// <para>
    /// Область жизни выбрана не по привычке. <c>Scoped</c> — почти всё: сервис работает с
    /// <c>DbContext</c> текущего запроса. <c>Singleton</c> — то, что переживает запрос по существу:
    /// очереди к фоновым процессам (<c>TranscodeQueue</c>, <c>EventIngestQueue</c>,
    /// <c>RecommendationRefreshQueue</c>), реестр играющих устройств и счётчики метрик. Именно эти
    /// синглтоны держат состояние в памяти процесса — из-за них приложение рассчитано ровно на один
    /// экземпляр (см. docs/backend/adr/0027-single-instance-deployment.md).
    /// </para>
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TranscodeQueue>();
        services.AddSingleton<PlaybackSessionRegistry>();

        services.AddMemoryCache();
        services.AddSingleton<RecommendationMetrics>();
        services.AddSingleton<EventIngestQueue>();
        services.AddSingleton<RecommendationRefreshQueue>();

        services.AddScoped<EventIngestService>();
        services.AddScoped<ProfileRollupService>();
        services.AddScoped<CandidateGenerator>();
        services.AddScoped<ShelfGenerationService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<RecommendationDiagnosticsService>();

        services.AddScoped<RadioService>();

        services.AddScoped<OutboundJobQueue>();
        services.AddScoped<ScrobbleQueueing>();
        services.AddScoped<LastfmService>();

        services.AddScoped<AuthService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<UserSettingsService>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<LyricsService>();
        services.AddScoped<CatalogService>();
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
