using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Imaging;
using MusicStreaming.Infrastructure.Metadata;
using MusicStreaming.Infrastructure.Persistence;
using MusicStreaming.Infrastructure.Security;
using MusicStreaming.Infrastructure.Storage;

namespace MusicStreaming.Infrastructure;

public static class DependencyInjection
{
    private const int MinSigningKeyBytes = 32;

    private static readonly HashSet<string> LeakedSigningKeys = new(StringComparer.Ordinal)
    {
        "2QAkr9k7Rr8J7YtZx/pPxuf1dbIRCB3rz2/lmJiHrR1chcApv8JZpPp2D7jT8ob+",
    };

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey),
                "Jwt:SigningKey is required. Set JWT_SIGNING_KEY in .env, or use dotnet user-secrets for local development.")
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= MinSigningKeyBytes,
                $"Jwt:SigningKey must be at least {MinSigningKeyBytes} bytes. Generate one with: openssl rand -base64 48")
            .Validate(
                o => !LeakedSigningKeys.Contains(o.SigningKey.Trim()),
                "Jwt:SigningKey is a known-leaked value that was published to a public repository. Generate a new one with: openssl rand -base64 48")
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

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<DatabaseInitializer>();

        services.AddSingleton<IMusicStorage, FileSystemMusicStorage>();
        services.AddSingleton<IAudioMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }
}
