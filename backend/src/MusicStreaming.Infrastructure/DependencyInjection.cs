// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;
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
        AudioDbOptions.Validated(services.Bind<AudioDbOptions>(configuration, AudioDbOptions.SectionName)).ValidateOnStart();
        LrclibOptions.Validated(services.Bind<LrclibOptions>(configuration, LrclibOptions.SectionName)).ValidateOnStart();

        // Без правил: в каждой из секций один флаг, проверять в нём нечего.
    }

    private static OptionsBuilder<T> Bind<T>(
        this IServiceCollection services, IConfiguration configuration, string section)
        where T : class =>
        services.AddOptions<T>().Bind(configuration.GetSection(section));

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        // optionsLifetime: Singleton обязателен рядом с AddDbContextFactory ниже — иначе фабрика
        // (синглтон) пытается получить scoped-опции, и контейнер падает при проверке на старте.
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention(),
            optionsLifetime: ServiceLifetime.Singleton);

        // Фабрика рядом с обычной регистрацией: нужна там, где независимые выборки идут
        // параллельно, а один контекст на них делить нельзя.
        services.AddDbContextFactory<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention(),
            lifetime: ServiceLifetime.Singleton);

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<IApplicationDbContextFactory, ApplicationDbContextFactory>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<LibraryMaintenance>();
    }

    private static void AddAdapters(this IServiceCollection services)
    {
        services.AddSingleton<StorageRoot>();
        services.AddSingleton<IMusicStorage, FileSystemMusicStorage>();
        services.AddSingleton<IImageStorage, FileSystemImageStorage>();
        services.AddSingleton<IHlsStorage, FileSystemHlsStorage>();
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAudioTranscoder, FfmpegAudioTranscoder>();

        // Индекс эмбеддингов — синглтон: 50k x 512 float это 100 МБ, которые незачем ни
        // перечитывать на запрос, ни держать в нескольких копиях.
        services.AddSingleton<EmbeddingIndex>();
        services.AddSingleton<IEmbeddingIndex>(provider => provider.GetRequiredService<EmbeddingIndex>());
        // CLAP под ONNX Runtime. Модель обязательна: без неё AudioEmbeddingWorker не даст
        // хосту подняться (см. ClapAudioEmbedder).
        services.AddSingleton<IAudioEmbedder, ClapAudioEmbedder>();
    }

    private static void AddIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
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
        services.AddHostedService<TranscodeWorker>();
        services.AddHostedService<TranscodeBackfillService>();
        services.AddHostedService<AudioEmbeddingWorker>();
        services.AddHostedService<EventIngestWorker>();
        services.AddHostedService<RecommendationWorker>();
        services.AddHostedService<LibraryMaintenanceWorker>();
        services.AddHostedService<EmbeddingIndexLoader>();
        services.AddHostedService<LibraryEnrichmentWorker>();
    }
}
