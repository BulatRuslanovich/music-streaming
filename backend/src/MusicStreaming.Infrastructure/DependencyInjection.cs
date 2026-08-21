// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey is required. Set JWT_SIGNING_KEY in .env, or use dotnet user-secrets for local development.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, $"Jwt:SigningKey must be at least 32 bytes. Generate one with: openssl rand -base64 48")
            .Validate(o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be greater than zero.")
            .Validate(o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "Storage:RootPath is required.")
            .Validate(o => o.MaxUploadBytes > 0, "Storage:MaxUploadBytes must be greater than zero.")
            .Validate(o => o.MaxImageUploadBytes > 0, "Storage:MaxImageUploadBytes must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<PlaybackOptions>()
            .Bind(configuration.GetSection(PlaybackOptions.SectionName))
            .Validate(o => o.HistoryThresholdSeconds > 0, "Playback:HistoryThresholdSeconds must be greater than zero.")
            .Validate(o => o.HistoryRetentionEntries > 0, "Playback:HistoryRetentionEntries must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<RecommendationOptions>()
            .Bind(configuration.GetSection(RecommendationOptions.SectionName))
            .Validate(o => o.TrackHalfLifeDays > 0 && o.ArtistHalfLifeDays > 0 && o.GenreHalfLifeDays > 0, "Recommendations half-lives must be greater than zero.")
            .Validate(o => o.ScoreSoftness > 0, "Recommendations:ScoreSoftness must be greater than zero.")
            .Validate(o => o.WarmThreshold >= 0 && o.MatureThreshold > o.WarmThreshold, "Recommendations:MatureThreshold must be greater than Recommendations:WarmThreshold.")
            .Validate(o => o.ShelfSize > 0, "Recommendations:ShelfSize must be greater than zero.")
            .Validate(o => o.CandidateLimit >= o.ShelfSize, "Recommendations:CandidateLimit must be at least Recommendations:ShelfSize.")
            .Validate(o => o.ExplorationRatio is >= 0 and <= 1, "Recommendations:ExplorationRatio must be between 0 and 1.")
            .Validate(o => o.DiscoveryExplorationRatio is >= 0 and <= 1, "Recommendations:DiscoveryExplorationRatio must be between 0 and 1.")
            .Validate(o => o.DiversityLambda is >= 0 and < 1, "Recommendations:DiversityLambda must be at least 0 and below 1.")
            .Validate(o => o.MaxPerArtist > 0 && o.MaxPerAlbum > 0 && o.MaxPerGenre > 0, "Recommendations per-shelf caps must be greater than zero.")
            .Validate(o => o.CacheTtlHours > 0, "Recommendations:CacheTtlHours must be greater than zero.")
            .Validate(o => o.EventRetentionDays > 0, "Recommendations:EventRetentionDays must be greater than zero.")
            .Validate(o => o.MaxEventsPerRequest > 0, "Recommendations:MaxEventsPerRequest must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<TranscodeOptions>()
            .Bind(configuration.GetSection(TranscodeOptions.SectionName))
            .Validate(
                o => o.LowBitrateKbps is >= 32 and <= 320
                     && o.NormalBitrateKbps is >= 32 and <= 320
                     && o.HighBitrateKbps is >= 32 and <= 320,
                "Transcode bitrates must be between 32 and 320.")
            .Validate(
                o => o.LowBitrateKbps <= o.NormalBitrateKbps && o.NormalBitrateKbps <= o.HighBitrateKbps,
                "Transcode bitrates must not decrease from Low to High.")
            .Validate(
                o => o.HlsSegmentSeconds is >= 2 and <= 10,
                "Transcode:HlsSegmentSeconds must be between 2 and 10.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.FfmpegPath),
                "Transcode:FfmpegPath is required.")
            .ValidateOnStart();

        services.AddOptions<AudioDbOptions>()
            .Bind(configuration.GetSection(AudioDbOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "AudioDb:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "AudioDb:BaseUrl is required.")
            .Validate(o => o.RequestDelayMs >= 0, "AudioDb:RequestDelayMs cannot be negative.")
            .ValidateOnStart();

        services.AddOptions<LrclibOptions>()
            .Bind(configuration.GetSection(LrclibOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "Lrclib:BaseUrl is required.")
            .Validate(o => o.RequestDelayMs >= 0, "Lrclib:RequestDelayMs cannot be negative.")
            .Validate(o => o.DurationToleranceSeconds >= 0, "Lrclib:DurationToleranceSeconds cannot be negative.")
            .ValidateOnStart();

        services.AddOptions<LibraryEnrichmentOptions>()
            .Bind(configuration.GetSection(LibraryEnrichmentOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<SimilarityMaintenance>();

        services.AddSingleton<IMusicStorage, FileSystemMusicStorage>();
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAudioTranscoder, FfmpegAudioTranscoder>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddOptions<LastfmOptions>().Bind(configuration.GetSection(LastfmOptions.SectionName));

        services.AddHttpClient<ILastfmApi, LastfmClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
        });

        services.AddHttpClient<IArtistImageProvider, TheAudioDbClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
        });
        services.AddHttpClient(TheAudioDbClient.ImageClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
        });
        services.AddHttpClient<ILyricsProvider, LrclibClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
        });

        services.AddHostedService<CoverBackfillService>();
        services.AddHostedService<TranscodeWorker>();
        services.AddHostedService<EventIngestWorker>();
        services.AddHostedService<RecommendationWorker>();
        services.AddHostedService<LibraryMaintenanceWorker>();
        services.AddHostedService<OutboundJobWorker>();
        services.AddHostedService<LibraryEnrichmentWorker>();

        return services;
    }
}
