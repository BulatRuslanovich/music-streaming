// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Api.Startup;

public static class LimitsSetup
{
    private const long MultipartOverhead = 1024 * 1024;

    private const int MaxFormValueLength = 64 * 1024;

    public static WebApplicationBuilder AddApiUploadLimits(this WebApplicationBuilder builder)
    {
        var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                      ?? new StorageOptions();

        var ceiling = storage.MaxUploadBytes + MultipartOverhead;

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = ceiling;
            options.ValueLengthLimit = MaxFormValueLength;
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
