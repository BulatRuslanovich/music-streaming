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
            .Validate(o => o.ProfileHalfLifeDays > 0, "Recommendations:ProfileHalfLifeDays must be greater than zero.")
            .Validate(o => o.HighSkipRateThreshold is >= 0 and < 1, "Recommendations:HighSkipRateThreshold must be at least 0 and below 1.")
            .Validate(o => o.HighSkipRatePenalty is > 0 and <= 1, "Recommendations:HighSkipRatePenalty must be above 0 and at most 1.")
            .Validate(o => o.MinimumStatsSupport > 0, "Recommendations:MinimumStatsSupport must be greater than zero.")
            .Validate(o => o.EraFitFloor is > 0 and <= 1, "Recommendations:EraFitFloor must be above 0 and at most 1.")
            .Validate(o => o.MinimumYearSpread > 0, "Recommendations:MinimumYearSpread must be greater than zero.")
            .Validate(o => o.ScoreSoftness > 0, "Recommendations:ScoreSoftness must be greater than zero.")
            .Validate(o => o.WarmThreshold >= 0 && o.MatureThreshold > o.WarmThreshold, "Recommendations:MatureThreshold must be greater than Recommendations:WarmThreshold.")
            .Validate(o => o.ShelfSize > 0, "Recommendations:ShelfSize must be greater than zero.")
            .Validate(o => o.CandidateLimit >= o.ShelfSize, "Recommendations:CandidateLimit must be at least Recommendations:ShelfSize.")
            .Validate(
                o => o.RegenerationMaxDelaySeconds >= o.RegenerationDebounceSeconds,
                "Recommendations:RegenerationMaxDelaySeconds must be at least Recommendations:RegenerationDebounceSeconds.")
            .Validate(o => o.TrackSuppressionDays >= 0, "Recommendations:TrackSuppressionDays must not be negative.")
            .Validate(o => o.DaypartWindowDays > 0, "Recommendations:DaypartWindowDays must be positive.")
            .Validate(
                o => o.MinimumDaypartShare is >= 0 and <= 1,
                "Recommendations:MinimumDaypartShare must be between 0 and 1.")
            .Validate(o => o.ExplorationRatio is >= 0 and <= 1, "Recommendations:ExplorationRatio must be between 0 and 1.")
            .Validate(o => o.DiscoveryExplorationRatio is >= 0 and <= 1, "Recommendations:DiscoveryExplorationRatio must be between 0 and 1.")
            .Validate(o => o.DiversityLambda is >= 0 and < 1, "Recommendations:DiversityLambda must be at least 0 and below 1.")
            .Validate(o => o.MultiSourceBonus is >= 0 and <= 0.5, "Recommendations:MultiSourceBonus must be between 0 and 0.5.")
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
            .Validate(
                o => o.BackfillBatchSize is >= 1 and <= 64,
                "Transcode:BackfillBatchSize must be between 1 and 64.")
            .Validate(
                o => o.BackfillPauseSeconds is >= 1 and <= 3600,
                "Transcode:BackfillPauseSeconds must be between 1 and 3600.")
            .Validate(
                o => o.BackfillStartupDelaySeconds is >= 0 and <= 3600,
                "Transcode:BackfillStartupDelaySeconds must be between 0 and 3600.")
            .ValidateOnStart();

        services.AddOptions<AudioAnalysisOptions>()
            .Bind(configuration.GetSection(AudioAnalysisOptions.SectionName))
            .Validate(o => o.SampleRateHz is >= 4000 and <= 48000,
                "AudioAnalysis:SampleRateHz must be between 4000 and 48000.")
            .Validate(o => o.MaximumSeconds is >= 30 and <= 3600,
                "AudioAnalysis:MaximumSeconds must be between 30 and 3600.")
            .Validate(o => o.BackfillBatchSize is >= 1 and <= 64,
                "AudioAnalysis:BackfillBatchSize must be between 1 and 64.")
            .Validate(o => o.PollSeconds is >= 5 and <= 3600,
                "AudioAnalysis:PollSeconds must be between 5 and 3600.")
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

        services.AddOptions<LibraryImportOptions>()
            .Bind(configuration.GetSection(LibraryImportOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Directory), "LibraryImport:Directory is required.")
            .Validate(o => !Path.IsPathRooted(o.Directory), "LibraryImport:Directory must be relative to Storage:RootPath.")
            .Validate(o => o.ScanIntervalSeconds is >= 30 and <= 86400, "LibraryImport:ScanIntervalSeconds must be between 30 and 86400.")
            .Validate(o => o.StartupDelaySeconds is >= 0 and <= 3600, "LibraryImport:StartupDelaySeconds must be between 0 and 3600.")
            .Validate(o => o.BatchSize is >= 1 and <= 1000, "LibraryImport:BatchSize must be between 1 and 1000.")
            .Validate(o => o.MinimumAgeSeconds is >= 0 and <= 3600, "LibraryImport:MinimumAgeSeconds must be between 0 and 3600.")
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .Validate(o => o.LoginAttemptsPerMinute > 0, "Security:LoginAttemptsPerMinute must be greater than zero.")
            .Validate(o => o.UploadsPerMinute > 0, "Security:UploadsPerMinute must be greater than zero.")
            .Validate(o => o.SearchesPerMinute > 0, "Security:SearchesPerMinute must be greater than zero.")
            .Validate(o => o.EventsPerMinute > 0, "Security:EventsPerMinute must be greater than zero.")
            .Validate(o => o.AccountLockoutAttempts >= 0, "Security:AccountLockoutAttempts cannot be negative.")
            .Validate(o => o.AccountLockoutMinutes > 0, "Security:AccountLockoutMinutes must be greater than zero.")
            .ValidateOnStart();

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
        services.AddSingleton<IImportSource, FileSystemImportSource>();
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IAudioTranscoder, FfmpegAudioTranscoder>();
        services.AddSingleton<IAudioFeatureAnalyzer, FfmpegAudioFeatureAnalyzer>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        services.AddOptions<LastfmOptions>().Bind(configuration.GetSection(LastfmOptions.SectionName));

        services.AddHttpClient<ILastfmApi, LastfmClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Caimack/1.0");
        });

        services.AddOptions<TagEnrichmentOptions>()
            .Bind(configuration.GetSection(TagEnrichmentOptions.SectionName))
            .Validate(o => o.MaxTagsPerEntity > 0, "TagEnrichment:MaxTagsPerEntity must be positive.")
            .Validate(
                o => o.MinimumTagWeight is >= 0 and <= 1,
                "TagEnrichment:MinimumTagWeight must be between 0 and 1.")
            .Validate(o => o.BackfillBatchSize >= 0, "TagEnrichment:BackfillBatchSize cannot be negative.")
            .Validate(o => o.RequestDelayMs >= 0, "TagEnrichment:RequestDelayMs cannot be negative.")
            .Validate(o => o.RefreshAfterDays > 0, "TagEnrichment:RefreshAfterDays must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<IMusicTagProvider, LastfmTagProvider>(client =>
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

        services.AddHostedService<TagBackfillWorker>();
        services.AddHostedService<CoverBackfillService>();
        services.AddHostedService<TranscodeWorker>();
        services.AddHostedService<TranscodeBackfillService>();
        services.AddHostedService<AudioAnalysisWorker>();
        services.AddHostedService<EventIngestWorker>();
        services.AddHostedService<RecommendationWorker>();
        services.AddHostedService<LibraryMaintenanceWorker>();
        services.AddHostedService<OutboundJobWorker>();
        services.AddHostedService<LibraryEnrichmentWorker>();
        services.AddHostedService<LibraryImportWorker>();

        return services;
    }
}
