// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Audio;
using MusicStreaming.Infrastructure.Imaging;
using MusicStreaming.Infrastructure.Integrations;
using MusicStreaming.Infrastructure.Metadata;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Recommendations;
using MusicStreaming.Infrastructure.Security;
using MusicStreaming.Infrastructure.Storage;

namespace MusicStreaming.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptionsFor(configuration);
        services.AddPersistence(configuration);
        services.AddAdapters();
        services.AddIntegrations(configuration);
        services.AddWorkers();

        return services;
    }

    /// <summary>
    /// Само правило валидации живёт рядом со свойством, которое оно охраняет — в
    /// <c>Application/Options</c>. Здесь остаётся только привязка к секции конфигурации.
    /// </summary>
    private static void AddOptionsFor(this IServiceCollection services, IConfiguration configuration)
    {
        JwtOptions.Validated(services.Bind<JwtOptions>(configuration, JwtOptions.SectionName)).ValidateOnStart();
        StorageOptions.Validated(services.Bind<StorageOptions>(configuration, StorageOptions.SectionName)).ValidateOnStart();
        PlaybackOptions.Validated(services.Bind<PlaybackOptions>(configuration, PlaybackOptions.SectionName)).ValidateOnStart();
        RecommendationOptions.Validated(services.Bind<RecommendationOptions>(configuration, RecommendationOptions.SectionName)).ValidateOnStart();
        TranscodeOptions.Validated(services.Bind<TranscodeOptions>(configuration, TranscodeOptions.SectionName)).ValidateOnStart();
        AudioAnalysisOptions.Validated(services.Bind<AudioAnalysisOptions>(configuration, AudioAnalysisOptions.SectionName)).ValidateOnStart();
        AudioDbOptions.Validated(services.Bind<AudioDbOptions>(configuration, AudioDbOptions.SectionName)).ValidateOnStart();
        LrclibOptions.Validated(services.Bind<LrclibOptions>(configuration, LrclibOptions.SectionName)).ValidateOnStart();
        LibraryImportOptions.Validated(services.Bind<LibraryImportOptions>(configuration, LibraryImportOptions.SectionName)).ValidateOnStart();
        SecurityOptions.Validated(services.Bind<SecurityOptions>(configuration, SecurityOptions.SectionName)).ValidateOnStart();
        TagEnrichmentOptions.Validated(services.Bind<TagEnrichmentOptions>(configuration, TagEnrichmentOptions.SectionName)).ValidateOnStart();

        // Без правил: обе секции целиком необязательны.
        services.Bind<LibraryEnrichmentOptions>(configuration, LibraryEnrichmentOptions.SectionName);
        services.Bind<LastfmOptions>(configuration, LastfmOptions.SectionName);
    }

    private static OptionsBuilder<T> Bind<T>(
        this IServiceCollection services, IConfiguration configuration, string section)
        where T : class =>
        services.AddOptions<T>().Bind(configuration.GetSection(section));

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<SimilarityMaintenance>();
    }

    private static void AddAdapters(this IServiceCollection services)
    {
        services.AddSingleton<IMusicStorage, FileSystemMusicStorage>();
        services.AddSingleton<IImportSource, FileSystemImportSource>();
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAudioTranscoder, FfmpegAudioTranscoder>();
        services.AddSingleton<IAudioFeatureAnalyzer, FfmpegAudioFeatureAnalyzer>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
    }

    private static void AddIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<ILastfmApi, LastfmClient>(Caimack(seconds: 10));
        services.AddHttpClient<IMusicTagProvider, LastfmTagProvider>(Caimack(seconds: 10));
        services.AddHttpClient<IArtistImageProvider, TheAudioDbClient>(Caimack(seconds: 15));
        services.AddHttpClient(TheAudioDbClient.ImageClientName, Caimack(seconds: 20));
        services.AddHttpClient<ILyricsProvider, LrclibClient>(Caimack(seconds: 15));
    }

    private static Action<HttpClient> Caimack(int seconds) => client =>
    {
        client.Timeout = TimeSpan.FromSeconds(seconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
    };

    private static void AddWorkers(this IServiceCollection services)
    {
        services.AddHostedService<TagBackfillWorker>();
        services.AddHostedService<CoverBackfillService>();
        services.AddHostedService<ImageRenditionBackfillService>();
        services.AddHostedService<TranscodeWorker>();
        services.AddHostedService<TranscodeBackfillService>();
        services.AddHostedService<AudioAnalysisWorker>();
        services.AddHostedService<EventIngestWorker>();
        services.AddHostedService<RecommendationWorker>();
        services.AddHostedService<LibraryMaintenanceWorker>();
        services.AddHostedService<OutboundJobWorker>();
        services.AddHostedService<LibraryEnrichmentWorker>();
        services.AddHostedService<LibraryImportWorker>();
    }
}
