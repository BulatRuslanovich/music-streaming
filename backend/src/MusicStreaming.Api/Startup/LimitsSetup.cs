using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Api.Startup;

public static class LimitsSetup
{
    private const int UploadBatchAllowance = 20;

    public static WebApplicationBuilder AddApiUploadLimits(this WebApplicationBuilder builder)
    {
        var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                      ?? new StorageOptions();

        var ceiling = storage.MaxUploadBytes * UploadBatchAllowance;

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = ceiling;
            options.ValueLengthLimit = int.MaxValue;
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = ceiling;
            options.Limits.MinRequestBodyDataRate = null;
        });

        builder.Services
            .AddDataProtection()
            .SetApplicationName("music-streaming")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storage.RootPath, ".dataprotection")));

        return builder;
    }
}
