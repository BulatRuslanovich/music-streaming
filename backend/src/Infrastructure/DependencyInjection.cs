// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
            .UseSnakeCaseNamingConvention());

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<LibraryMaintenance>();

        services.AddSingleton<StorageRoot>();
        services.AddSingleton<FileSystemMusicStorage>();
        services.AddSingleton<FileSystemImageStorage>();
        services.AddSingleton<FileSystemHlsStorage>();
        services.AddSingleton<TagLibAudioMetadataReader>();
        services.AddSingleton<ImageSharpImageProcessor>();
        services.AddSingleton<BCryptPasswordHasher>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<FfmpegAudioTranscoder>();

        services.AddSingleton<ClapAudioEmbedder>();

        services.AddHttpClient<DeezerClient>(Caimack(seconds: 15));
        services.AddHttpClient(DeezerClient.ImageClientName, Caimack(seconds: 20));
        services.AddHttpClient<LrclibClient>(Caimack(seconds: 15));

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
