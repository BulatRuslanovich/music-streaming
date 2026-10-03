// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using App.Abstractions;
using App.Options;
using Infrastructure.Audio;
using Infrastructure.Imaging;
using Infrastructure.Integrations;
using Infrastructure.Metadata;
using Infrastructure.Persistence;
using Infrastructure.Recommendations;
using Infrastructure.Security;
using Infrastructure.Storage;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        JwtOptions.Validated(services.Bind<JwtOptions>(configuration, JwtOptions.SectionName)).ValidateOnStart();
        StorageOptions.Validated(services.Bind<StorageOptions>(configuration, StorageOptions.SectionName)).ValidateOnStart();
        LrclibOptions.Validated(services.Bind<LrclibOptions>(configuration, LrclibOptions.SectionName)).ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention(),
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention(),
            lifetime: ServiceLifetime.Singleton);

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<IApplicationDbContextFactory, ApplicationDbContextFactory>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<LibraryMaintenance>();

        services.AddSingleton<StorageRoot>();
        services.AddSingleton<IMusicStorage, FileSystemMusicStorage>();
        services.AddSingleton<IImageStorage, FileSystemImageStorage>();
        services.AddSingleton<IHlsStorage, FileSystemHlsStorage>();
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAudioTranscoder, FfmpegAudioTranscoder>();

        services.AddSingleton<IAudioEmbedder, ClapAudioEmbedder>();

        services.AddHttpClient<IArtistImageProvider, DeezerClient>(Caimack(seconds: 15));
        services.AddHttpClient(DeezerClient.ImageClientName, Caimack(seconds: 20));
        services.AddHttpClient<ILyricsProvider, LrclibClient>(Caimack(seconds: 15));

        services.AddHostedService<TranscodeWorker>();
        services.AddHostedService<TranscodeBackfillService>();
        services.AddHostedService<AudioEmbeddingWorker>();
        services.AddHostedService<RecommendationWorker>();
        services.AddHostedService<LibraryMaintenanceWorker>();
        services.AddHostedService<EmbeddingIndexLoader>();
        services.AddHostedService<LibraryEnrichmentWorker>();

        return services;
    }

    private static OptionsBuilder<T> Bind<T>(
        this IServiceCollection services, IConfiguration configuration, string section)
        where T : class =>
        services.AddOptions<T>().Bind(configuration.GetSection(section));

    private static Action<HttpClient> Caimack(int seconds) => client =>
    {
        client.Timeout = TimeSpan.FromSeconds(seconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
    };
}
